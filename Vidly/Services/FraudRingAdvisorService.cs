using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // -- Enums --------------------------------------------------------

    /// <summary>Per-ring escalation verdict, ordered by severity.</summary>
    public enum RingVerdict
    {
        None = 0,
        Monitor = 1,
        Investigate = 2,
        FreezeAndReview = 3,
        RecommendBan = 4
    }

    /// <summary>Priority bucket for ring-level recommended actions.</summary>
    public enum RingPriority { P0, P1, P2, P3 }

    /// <summary>
    /// Risk-appetite knob. Cautious raises ring scores (escalates earlier);
    /// Aggressive lowers them and trims low-priority playbook entries.
    /// </summary>
    public enum FraudRingRiskAppetite { Cautious, Balanced, Aggressive }

    // -- Config -------------------------------------------------------

    /// <summary>
    /// Tunable thresholds for <see cref="FraudRingAdvisorService"/>. All knobs
    /// are explicit so the historically-magic constants in the detection rules
    /// are discoverable in one place.
    /// </summary>
    public class FraudRingAdvisorConfig
    {
        public int LookbackDays { get; set; } = 90;
        public int MinRingSize { get; set; } = 2;
        public int MaxRingSize { get; set; } = 12;

        public int SharedPhonePrefixLength { get; set; } = 7;
        public int SharedEmailDomainMin { get; set; } = 3;

        public HashSet<string> CommonEmailDomains { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gmail.com","yahoo.com","hotmail.com","outlook.com",
            "icloud.com","aol.com","proton.me","protonmail.com"
        };

        public int CoOverlapMinDays { get; set; } = 0;
        public int CoMovieSharedMin { get; set; } = 2;
        public int RapidSequenceWindowHours { get; set; } = 24;
        public int NewAccountWindowDays { get; set; } = 30;
        public int HighDamagePatternMin { get; set; } = 2;
        public int LateReturnPatternMin { get; set; } = 2;

        public int RingFraudFloorScore { get; set; } = 35;
        public int RingInvestigateScore { get; set; } = 55;
        public int RingFreezeScore { get; set; } = 75;
        public int RingBanScore { get; set; } = 90;

        public int MaxPlaybookActions { get; set; } = 12;
        public FraudRingRiskAppetite RiskAppetite { get; set; } = FraudRingRiskAppetite.Balanced;
    }

    // -- Models -------------------------------------------------------

    public class RingSignal
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string Severity { get; set; } // "info" / "warn" / "critical"
        public string Reason { get; set; }
        public int Weight { get; set; }
    }

    public class RingMember
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public int RentalsInWindow { get; set; }
        public decimal DollarsAtRisk { get; set; }
        public bool IsNewAccount { get; set; }
    }

    public class FraudRing
    {
        public string RingId { get; set; }
        public List<RingMember> Members { get; set; } = new List<RingMember>();
        public RingVerdict Verdict { get; set; }
        public RingPriority Priority { get; set; }
        public int Score { get; set; }
        public List<RingSignal> Signals { get; set; } = new List<RingSignal>();
        public string RecommendedActionId { get; set; }
        public string Reason { get; set; }
        public List<int> SharedMovieIds { get; set; } = new List<int>();
        public List<string> SharedAttributes { get; set; } = new List<string>();
    }

    public class RingPlaybookAction
    {
        public string Id { get; set; }
        public RingPriority Priority { get; set; }
        public string Label { get; set; }
        public string Owner { get; set; } // loss_prevention | manager | billing | legal | system
        public int BlastRadius { get; set; } // 1..5
        public string Reversibility { get; set; } // low | medium | high
        public string Reason { get; set; }
        public List<string> RingIds { get; set; } = new List<string>();
    }

    public class FraudRingPortfolioSummary
    {
        public int RingCount { get; set; }
        public int TotalCustomersInRings { get; set; }
        public decimal TotalDollarsAtRisk { get; set; }
        public int P0Count { get; set; }
        public int P1Count { get; set; }
        public int P2Count { get; set; }
        public int OverallScore { get; set; }
        public string Grade { get; set; }
        public string Headline { get; set; }
    }

    public class FraudRingInsight
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
    }

    public class FraudRingReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<FraudRing> Rings { get; set; } = new List<FraudRing>();
        public List<RingPlaybookAction> Playbook { get; set; } = new List<RingPlaybookAction>();
        public List<FraudRingInsight> Insights { get; set; } = new List<FraudRingInsight>();
        public FraudRingPortfolioSummary Summary { get; set; } = new FraudRingPortfolioSummary();

        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[{0}] FraudRingAdvisor: {1} rings, {2} customers, ${3} at risk - P0={4}, P1={5}",
                Summary.Grade ?? "A",
                Summary.RingCount,
                Summary.TotalCustomersInRings,
                Summary.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture),
                Summary.P0Count,
                Summary.P1Count);
            sb.AppendLine();
            foreach (var r in Rings.Take(10))
            {
                var memberCsv = string.Join(",", r.Members.Select(m => m.CustomerName ?? ("#" + m.CustomerId)));
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[{0}] {1} {2}/100 ({3}): {4}",
                    r.Priority, r.RingId, r.Score, r.Verdict, memberCsv);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public string RenderMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Summary");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "- Grade **{0}** | Rings: {1} | Customers: {2} | Dollars at risk: ${3}",
                Summary.Grade ?? "A",
                Summary.RingCount,
                Summary.TotalCustomersInRings,
                Summary.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "- P0={0}, P1={1}, P2={2}, Overall score: {3}/100",
                Summary.P0Count, Summary.P1Count, Summary.P2Count, Summary.OverallScore);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(Summary.Headline))
            {
                sb.AppendLine("- " + Esc(Summary.Headline));
            }
            sb.AppendLine();

            sb.AppendLine("## Rings");
            sb.AppendLine("| RingId | Members | Verdict | Priority | Score | SharedAttributes |");
            sb.AppendLine("|--------|---------|---------|----------|-------|------------------|");
            if (Rings.Count == 0)
            {
                sb.AppendLine("| _(none)_ |  |  |  |  |  |");
            }
            else
            {
                foreach (var r in Rings)
                {
                    var members = string.Join(", ", r.Members.Select(m => Esc(m.CustomerName ?? ("#" + m.CustomerId))));
                    var attrs = string.Join(", ", r.SharedAttributes.Select(Esc));
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3} | {4} | {5} |",
                        Esc(r.RingId), members, r.Verdict, r.Priority, r.Score, attrs);
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Playbook");
            if (Playbook.Count == 0) sb.AppendLine("- _(none)_");
            else foreach (var p in Playbook)
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "- [{0}] {1} ({2}) - {3}{4}",
                    p.Priority, Esc(p.Label), Esc(p.Owner), Esc(p.Reason), Environment.NewLine);
            sb.AppendLine();

            sb.AppendLine("## Insights");
            if (Insights.Count == 0) sb.AppendLine("- _(none)_");
            else foreach (var i in Insights)
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "- **{0}**: {1}{2}",
                    Esc(i.Label), Esc(i.Detail), Environment.NewLine);

            return sb.ToString();
        }

        public string RenderJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  \"generatedAt\": \"{0}\",", GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            sb.AppendLine();

            // insights
            sb.AppendLine("  \"insights\": [");
            for (int i = 0; i < Insights.Count; i++)
            {
                var ins = Insights[i];
                sb.AppendLine("    {");
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"code\": \"{0}\",", J(ins.Code)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"detail\": \"{0}\",", J(ins.Detail)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"label\": \"{0}\"", J(ins.Label)); sb.AppendLine();
                sb.Append("    }" + (i < Insights.Count - 1 ? "," : "")); sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // playbook
            sb.AppendLine("  \"playbook\": [");
            for (int i = 0; i < Playbook.Count; i++)
            {
                var p = Playbook[i];
                sb.AppendLine("    {");
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"blastRadius\": {0},", p.BlastRadius); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"id\": \"{0}\",", J(p.Id)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"label\": \"{0}\",", J(p.Label)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"owner\": \"{0}\",", J(p.Owner)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"priority\": \"{0}\",", p.Priority); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"reason\": \"{0}\",", J(p.Reason)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"reversibility\": \"{0}\",", J(p.Reversibility)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"ringIds\": [{0}]", string.Join(",", p.RingIds.Select(r => "\"" + J(r) + "\"")));
                sb.AppendLine();
                sb.Append("    }" + (i < Playbook.Count - 1 ? "," : "")); sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // rings
            sb.AppendLine("  \"rings\": [");
            for (int i = 0; i < Rings.Count; i++)
            {
                var r = Rings[i];
                sb.AppendLine("    {");
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"members\": [");
                sb.AppendLine();
                for (int j = 0; j < r.Members.Count; j++)
                {
                    var m = r.Members[j];
                    sb.AppendLine("        {");
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"customerId\": {0},", m.CustomerId); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"customerName\": \"{0}\",", J(m.CustomerName)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"dollarsAtRisk\": {0},", m.DollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"email\": \"{0}\",", J(m.Email)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"isNewAccount\": {0},", m.IsNewAccount ? "true" : "false"); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"phone\": \"{0}\",", J(m.Phone)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"rentalsInWindow\": {0}", m.RentalsInWindow); sb.AppendLine();
                    sb.Append("        }" + (j < r.Members.Count - 1 ? "," : "")); sb.AppendLine();
                }
                sb.AppendLine("      ],");
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"priority\": \"{0}\",", r.Priority); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"reason\": \"{0}\",", J(r.Reason)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"recommendedActionId\": \"{0}\",", J(r.RecommendedActionId)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"ringId\": \"{0}\",", J(r.RingId)); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"score\": {0},", r.Score); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"sharedAttributes\": [{0}],",
                    string.Join(",", r.SharedAttributes.Select(s => "\"" + J(s) + "\""))); sb.AppendLine();
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"sharedMovieIds\": [{0}],",
                    string.Join(",", r.SharedMovieIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))); sb.AppendLine();
                sb.AppendLine("      \"signals\": [");
                for (int j = 0; j < r.Signals.Count; j++)
                {
                    var s = r.Signals[j];
                    sb.AppendLine("        {");
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"code\": \"{0}\",", J(s.Code)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"label\": \"{0}\",", J(s.Label)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"reason\": \"{0}\",", J(s.Reason)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"severity\": \"{0}\",", J(s.Severity)); sb.AppendLine();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "          \"weight\": {0}", s.Weight); sb.AppendLine();
                    sb.Append("        }" + (j < r.Signals.Count - 1 ? "," : "")); sb.AppendLine();
                }
                sb.AppendLine("      ],");
                sb.AppendFormat(CultureInfo.InvariantCulture, "      \"verdict\": \"{0}\"", r.Verdict); sb.AppendLine();
                sb.Append("    }" + (i < Rings.Count - 1 ? "," : "")); sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // summary
            sb.AppendLine("  \"summary\": {");
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"grade\": \"{0}\",", J(Summary.Grade)); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"headline\": \"{0}\",", J(Summary.Headline)); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"overallScore\": {0},", Summary.OverallScore); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"p0Count\": {0},", Summary.P0Count); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"p1Count\": {0},", Summary.P1Count); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"p2Count\": {0},", Summary.P2Count); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"ringCount\": {0},", Summary.RingCount); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"totalCustomersInRings\": {0},", Summary.TotalCustomersInRings); sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    \"totalDollarsAtRisk\": {0}", Summary.TotalDollarsAtRisk.ToString("F2", CultureInfo.InvariantCulture)); sb.AppendLine();
            sb.AppendLine("  }");
            sb.Append("}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", "\\|");
        }

        private static string J(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)ch);
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    // -- Service ------------------------------------------------------

    /// <summary>
    /// Cross-customer fraud-ring detector. While <see cref="FraudDetectorService"/>
    /// scores customers in isolation, this advisor surfaces small clusters of
    /// customers acting together (shared phone prefixes, throwaway email domains,
    /// rapid co-rentals of the same titles, simultaneous account creation, etc.)
    /// that no single-customer model would catch. Read-only: never mutates any
    /// repository or model.
    /// </summary>
    public class FraudRingAdvisorService
    {
        private readonly IRentalRepository _rentals;
        private readonly ICustomerRepository _customers;
        private readonly IClock _clock;
        private readonly FraudRingAdvisorConfig _config;

        public FraudRingAdvisorService(IRentalRepository rentals,
                                       ICustomerRepository customers,
                                       IClock clock,
                                       FraudRingAdvisorConfig config = null)
        {
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? new FraudRingAdvisorConfig();
        }

        public FraudRingReport GenerateReport()
        {
            var today = _clock.Today;
            var windowStart = today.AddDays(-_config.LookbackDays);
            var customers = (_customers.GetAll() ?? new List<Customer>()).ToList();

            // Pre-compute per-customer rentals-in-window snapshots.
            var perCust = new Dictionary<int, CustSnap>();
            foreach (var c in customers)
            {
                var rentals = (_rentals.GetByCustomer(c.Id) ?? new List<Rental>())
                    .Where(r => r.RentalDate >= windowStart)
                    .ToList();
                if (rentals.Count == 0) continue;
                perCust[c.Id] = new CustSnap
                {
                    Customer = c,
                    Rentals = rentals,
                    MovieIds = new HashSet<int>(rentals.Select(r => r.MovieId)),
                    IsNew = c.MemberSince.HasValue &&
                            (today - c.MemberSince.Value).TotalDays <= _config.NewAccountWindowDays,
                    DollarsAtRisk = rentals.Sum(r => Math.Max(0m, r.DailyRate * 5m) + r.LateFee + r.DamageCharge),
                    LateCount = rentals.Count(r => r.ReturnDate.HasValue
                        && (r.ReturnDate.Value - r.DueDate).TotalDays >= 5),
                    DamageCount = rentals.Count(r => r.DamageCharge > 0m || r.LateFee >= 5m)
                };
            }

            var ids = perCust.Keys.OrderBy(x => x).ToList();

            // Count email domains across full population.
            var domainCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cid in ids)
            {
                var d = ExtractDomain(perCust[cid].Customer.Email);
                if (string.IsNullOrEmpty(d) || _config.CommonEmailDomains.Contains(d)) continue;
                domainCounts[d] = (domainCounts.TryGetValue(d, out var v) ? v : 0) + 1;
            }

            // Build edges (pair -> list of signals with weights).
            var edges = new Dictionary<(int, int), List<RingSignal>>();
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var a = perCust[ids[i]];
                    var b = perCust[ids[j]];
                    var sigs = ScorePair(a, b, domainCounts);
                    if (sigs.Sum(s => s.Weight) >= 20)
                    {
                        edges[(ids[i], ids[j])] = sigs;
                    }
                }
            }

            // Union-find clustering.
            var parent = new Dictionary<int, int>();
            foreach (var id in ids) parent[id] = id;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { var rx = Find(x); var ry = Find(y); if (rx != ry) parent[rx] = ry; }
            foreach (var e in edges.Keys) Union(e.Item1, e.Item2);

            var clusters = new Dictionary<int, List<int>>();
            foreach (var id in ids)
            {
                if (!edges.Keys.Any(k => k.Item1 == id || k.Item2 == id)) continue;
                var root = Find(id);
                if (!clusters.TryGetValue(root, out var lst)) { lst = new List<int>(); clusters[root] = lst; }
                lst.Add(id);
            }

            var rings = new List<FraudRing>();
            foreach (var cluster in clusters.Values)
            {
                if (cluster.Count < _config.MinRingSize) continue;
                var members = cluster.OrderBy(x => x).Take(_config.MaxRingSize).ToList();
                var aggSignals = new Dictionary<string, RingSignal>();
                var sharedMovies = new HashSet<int>();
                var sharedAttrs = new HashSet<string>();
                foreach (var (pair, sigs) in edges.Select(kv => (kv.Key, kv.Value)))
                {
                    if (!members.Contains(pair.Item1) || !members.Contains(pair.Item2)) continue;
                    foreach (var s in sigs)
                    {
                        if (!aggSignals.ContainsKey(s.Code)) aggSignals[s.Code] = s;
                        if (s.Code == "CO_RENTED_MOVIES")
                        {
                            foreach (var mid in perCust[pair.Item1].MovieIds.Intersect(perCust[pair.Item2].MovieIds))
                                sharedMovies.Add(mid);
                        }
                        sharedAttrs.Add(s.Code);
                    }
                }

                var signalList = aggSignals.Values
                    .OrderByDescending(s => s.Weight).ThenBy(s => s.Code).ToList();
                int rawScore = Math.Min(100, signalList.Sum(s => s.Weight));
                double mult = _config.RiskAppetite == FraudRingRiskAppetite.Cautious ? 1.15
                             : _config.RiskAppetite == FraudRingRiskAppetite.Aggressive ? 0.85
                             : 1.0;
                int score = (int)Math.Round(Math.Max(0, Math.Min(100, rawScore * mult)));

                RingVerdict verdict;
                if (score >= _config.RingBanScore) verdict = RingVerdict.RecommendBan;
                else if (score >= _config.RingFreezeScore) verdict = RingVerdict.FreezeAndReview;
                else if (score >= _config.RingInvestigateScore) verdict = RingVerdict.Investigate;
                else if (score >= _config.RingFraudFloorScore) verdict = RingVerdict.Monitor;
                else continue;

                var pri = verdict == RingVerdict.RecommendBan ? RingPriority.P0
                        : verdict == RingVerdict.FreezeAndReview ? RingPriority.P1
                        : verdict == RingVerdict.Investigate ? RingPriority.P2
                        : RingPriority.P3;

                var actionId = (verdict == RingVerdict.RecommendBan || verdict == RingVerdict.FreezeAndReview)
                    ? "FREEZE_RING_ACCOUNTS"
                    : verdict == RingVerdict.Investigate ? "OPEN_INVESTIGATION_TICKET"
                    : "ADD_TO_WATCHLIST";

                var ringMembers = members.Select(mid =>
                {
                    var s = perCust[mid];
                    return new RingMember
                    {
                        CustomerId = mid,
                        CustomerName = s.Customer.Name,
                        Email = s.Customer.Email ?? "",
                        Phone = s.Customer.Phone ?? "",
                        RentalsInWindow = s.Rentals.Count,
                        DollarsAtRisk = Math.Round(s.DollarsAtRisk, 2),
                        IsNewAccount = s.IsNew
                    };
                }).OrderBy(m => m.CustomerId).ToList();

                rings.Add(new FraudRing
                {
                    RingId = "ring:" + string.Join("-", members.OrderBy(x => x)),
                    Members = ringMembers,
                    Verdict = verdict,
                    Priority = pri,
                    Score = score,
                    Signals = signalList,
                    RecommendedActionId = actionId,
                    Reason = string.Format(CultureInfo.InvariantCulture,
                        "{0} signals across {1} members (raw {2}, appetite {3})",
                        signalList.Count, ringMembers.Count, rawScore, _config.RiskAppetite),
                    SharedMovieIds = sharedMovies.OrderBy(x => x).ToList(),
                    SharedAttributes = sharedAttrs.OrderBy(x => x, StringComparer.Ordinal).ToList()
                });
            }

            rings = rings
                .OrderBy(r => r.Priority)
                .ThenByDescending(r => r.Score)
                .ThenBy(r => r.RingId, StringComparer.Ordinal)
                .ToList();

            var summary = BuildSummary(rings);
            var playbook = BuildPlaybook(rings, summary);
            var insights = BuildInsights(rings);

            return new FraudRingReport
            {
                GeneratedAt = _clock.Now,
                Rings = rings,
                Playbook = playbook,
                Insights = insights,
                Summary = summary
            };
        }

        // -- Helpers --------------------------------------------------

        private class CustSnap
        {
            public Customer Customer;
            public List<Rental> Rentals;
            public HashSet<int> MovieIds;
            public bool IsNew;
            public decimal DollarsAtRisk;
            public int LateCount;
            public int DamageCount;
        }

        private List<RingSignal> ScorePair(CustSnap a, CustSnap b, Dictionary<string, int> domainCounts)
        {
            var sigs = new List<RingSignal>();

            // SHARED_PHONE_PREFIX
            var pa = NormalizePhone(a.Customer.Phone);
            var pb = NormalizePhone(b.Customer.Phone);
            if (pa.Length >= _config.SharedPhonePrefixLength && pb.Length >= _config.SharedPhonePrefixLength
                && pa.Substring(0, _config.SharedPhonePrefixLength) == pb.Substring(0, _config.SharedPhonePrefixLength))
            {
                sigs.Add(new RingSignal
                {
                    Code = "SHARED_PHONE_PREFIX",
                    Label = "Shared phone prefix",
                    Severity = "warn",
                    Reason = "Customers share the first " + _config.SharedPhonePrefixLength + " digits of their phone numbers.",
                    Weight = 18
                });
            }

            // SHARED_EMAIL_DOMAIN
            var da = ExtractDomain(a.Customer.Email);
            var db = ExtractDomain(b.Customer.Email);
            if (!string.IsNullOrEmpty(da) && string.Equals(da, db, StringComparison.OrdinalIgnoreCase)
                && !_config.CommonEmailDomains.Contains(da)
                && domainCounts.TryGetValue(da, out var dc) && dc >= _config.SharedEmailDomainMin)
            {
                sigs.Add(new RingSignal
                {
                    Code = "SHARED_EMAIL_DOMAIN",
                    Label = "Shared uncommon email domain",
                    Severity = "warn",
                    Reason = "Both members use the uncommon domain '" + da + "', shared by " + dc + " customers.",
                    Weight = 12
                });
            }

            // CO_RENTED_MOVIES
            var shared = a.MovieIds.Intersect(b.MovieIds).ToList();
            if (shared.Count >= _config.CoMovieSharedMin)
            {
                sigs.Add(new RingSignal
                {
                    Code = "CO_RENTED_MOVIES",
                    Label = "Co-rented movies",
                    Severity = "info",
                    Reason = "Pair shares " + shared.Count + " rented movies within the lookback window.",
                    Weight = 14
                });
            }

            // RAPID_SEQUENCE
            bool rapid = false;
            foreach (var ra in a.Rentals)
            {
                foreach (var rb in b.Rentals)
                {
                    if (ra.MovieId != rb.MovieId) continue;
                    var hours = Math.Abs((ra.RentalDate - rb.RentalDate).TotalHours);
                    if (hours <= _config.RapidSequenceWindowHours) { rapid = true; break; }
                }
                if (rapid) break;
            }
            if (rapid)
            {
                sigs.Add(new RingSignal
                {
                    Code = "RAPID_SEQUENCE",
                    Label = "Rapid co-rental sequence",
                    Severity = "warn",
                    Reason = "Same movie rented by both members within " + _config.RapidSequenceWindowHours + "h.",
                    Weight = 16
                });
            }

            // SIMULTANEOUS_ACCOUNT_CREATION
            if (a.IsNew && b.IsNew)
            {
                sigs.Add(new RingSignal
                {
                    Code = "SIMULTANEOUS_ACCOUNT_CREATION",
                    Label = "Simultaneous account creation",
                    Severity = "info",
                    Reason = "Both members created accounts within " + _config.NewAccountWindowDays + " days.",
                    Weight = 10
                });
            }

            // LATE_RETURN_PATTERN
            if (a.LateCount >= _config.LateReturnPatternMin && b.LateCount >= _config.LateReturnPatternMin)
            {
                sigs.Add(new RingSignal
                {
                    Code = "LATE_RETURN_PATTERN",
                    Label = "Coordinated late returns",
                    Severity = "info",
                    Reason = "Both members have >=" + _config.LateReturnPatternMin + " late returns of 5+ days.",
                    Weight = 8
                });
            }

            // DAMAGE_OR_LOSS_PATTERN
            if (a.DamageCount >= _config.HighDamagePatternMin && b.DamageCount >= _config.HighDamagePatternMin)
            {
                sigs.Add(new RingSignal
                {
                    Code = "DAMAGE_OR_LOSS_PATTERN",
                    Label = "Coordinated damage/loss",
                    Severity = "warn",
                    Reason = "Both members have >=" + _config.HighDamagePatternMin + " damage/loss-bearing rentals.",
                    Weight = 12
                });
            }

            return sigs;
        }

        private static string NormalizePhone(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            var sb = new StringBuilder(p.Length);
            foreach (var ch in p) if (ch >= '0' && ch <= '9') sb.Append(ch);
            return sb.ToString();
        }

        private static string ExtractDomain(string email)
        {
            if (string.IsNullOrEmpty(email)) return "";
            var at = email.IndexOf('@');
            if (at < 0 || at >= email.Length - 1) return "";
            return email.Substring(at + 1).Trim().ToLowerInvariant();
        }

        private FraudRingPortfolioSummary BuildSummary(List<FraudRing> rings)
        {
            var sum = new FraudRingPortfolioSummary
            {
                RingCount = rings.Count,
                TotalCustomersInRings = rings.Sum(r => r.Members.Count),
                TotalDollarsAtRisk = Math.Round(rings.Sum(r => r.Members.Sum(m => m.DollarsAtRisk)), 2),
                P0Count = rings.Count(r => r.Priority == RingPriority.P0),
                P1Count = rings.Count(r => r.Priority == RingPriority.P1),
                P2Count = rings.Count(r => r.Priority == RingPriority.P2)
            };

            if (rings.Count == 0)
            {
                sum.OverallScore = 0;
                sum.Grade = "A";
                sum.Headline = "No fraud rings detected in lookback window.";
                return sum;
            }

            double wsum = 0; int wtot = 0;
            foreach (var r in rings)
            {
                wsum += (double)r.Score * r.Members.Count;
                wtot += r.Members.Count;
            }
            sum.OverallScore = wtot == 0 ? 0 : (int)Math.Round(wsum / wtot);

            string grade;
            if (sum.P0Count >= 1 && (sum.P1Count >= 3 || sum.OverallScore >= 80)) grade = "F";
            else if (sum.P0Count >= 1 || sum.OverallScore >= 80) grade = sum.OverallScore >= 80 ? "F" : "D";
            else if (sum.OverallScore >= 60) grade = "D";
            else if (sum.OverallScore >= 40 || sum.P1Count >= 1) grade = "C";
            else if (sum.OverallScore >= 20) grade = "B";
            else grade = "A";
            sum.Grade = grade;
            sum.Headline = string.Format(CultureInfo.InvariantCulture,
                "{0} ring(s) flagged, {1} customer(s) involved.", sum.RingCount, sum.TotalCustomersInRings);
            return sum;
        }

        private List<RingPlaybookAction> BuildPlaybook(List<FraudRing> rings, FraudRingPortfolioSummary summary)
        {
            var pb = new List<RingPlaybookAction>();
            var bans = rings.Where(r => r.Verdict == RingVerdict.RecommendBan).ToList();
            var freezes = rings.Where(r => r.Verdict == RingVerdict.FreezeAndReview).ToList();
            var invs = rings.Where(r => r.Verdict == RingVerdict.Investigate).ToList();
            var mons = rings.Where(r => r.Verdict == RingVerdict.Monitor).ToList();

            if (bans.Count >= 1)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "EMERGENCY_FREEZE_RING_BATCH",
                    Priority = RingPriority.P0,
                    Label = "Emergency freeze of confirmed-ban rings",
                    Owner = "loss_prevention",
                    BlastRadius = 5,
                    Reversibility = "low",
                    Reason = "One or more rings exceeded the ban threshold.",
                    RingIds = bans.Select(r => r.RingId).ToList()
                });
            }
            if (bans.Count >= 2)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "ESCALATE_TO_LEGAL",
                    Priority = RingPriority.P0,
                    Label = "Escalate to legal counsel",
                    Owner = "legal",
                    BlastRadius = 5,
                    Reversibility = "low",
                    Reason = "Multiple ring-bans suggest organized abuse.",
                    RingIds = bans.Select(r => r.RingId).ToList()
                });
            }
            foreach (var r in freezes)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "BATCH_FREEZE_FOR_REVIEW",
                    Priority = RingPriority.P1,
                    Label = "Batch freeze ring " + r.RingId,
                    Owner = "manager",
                    BlastRadius = 4,
                    Reversibility = "medium",
                    Reason = "Ring scored within freeze band.",
                    RingIds = new List<string> { r.RingId }
                });
            }

            // Root-cause shared infra if those signals dominate.
            var infraCodes = new HashSet<string> { "SHARED_PHONE_PREFIX", "SHARED_EMAIL_DOMAIN", "SHARED_ADDRESS" };
            var infraRings = rings.Where(r => r.Signals.Any(s => infraCodes.Contains(s.Code))).ToList();
            if (infraRings.Count >= 1 && infraRings.Sum(r => r.Signals.Count(s => infraCodes.Contains(s.Code)))
                >= Math.Max(2, rings.Sum(r => r.Signals.Count) / 3))
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "ROOT_CAUSE_SHARED_INFRASTRUCTURE",
                    Priority = RingPriority.P1,
                    Label = "Investigate shared phone/email/address infrastructure",
                    Owner = "loss_prevention",
                    BlastRadius = 3,
                    Reversibility = "high",
                    Reason = "Shared-infra signals dominate the detected rings.",
                    RingIds = infraRings.Select(r => r.RingId).ToList()
                });
            }

            foreach (var r in invs)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "OPEN_INVESTIGATION_BATCH",
                    Priority = RingPriority.P2,
                    Label = "Open investigation ticket for ring " + r.RingId,
                    Owner = "loss_prevention",
                    BlastRadius = 2,
                    Reversibility = "high",
                    Reason = "Ring landed within investigate band.",
                    RingIds = new List<string> { r.RingId }
                });
            }

            int newAcctRings = rings.Count(r => r.Signals.Any(s => s.Code == "SIMULTANEOUS_ACCOUNT_CREATION"));
            if (newAcctRings >= 2)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "TIGHTEN_NEW_ACCOUNT_VERIFICATION",
                    Priority = RingPriority.P2,
                    Label = "Tighten new-account verification gates",
                    Owner = "system",
                    BlastRadius = 3,
                    Reversibility = "high",
                    Reason = "Simultaneous new-account signals span multiple rings.",
                    RingIds = rings.Where(r => r.Signals.Any(s => s.Code == "SIMULTANEOUS_ACCOUNT_CREATION"))
                                   .Select(r => r.RingId).ToList()
                });
            }

            foreach (var r in mons)
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "WATCHLIST_MONITOR",
                    Priority = RingPriority.P3,
                    Label = "Monitor ring " + r.RingId,
                    Owner = "system",
                    BlastRadius = 1,
                    Reversibility = "high",
                    Reason = "Ring above monitor floor but below investigate.",
                    RingIds = new List<string> { r.RingId }
                });
            }

            if (_config.RiskAppetite == FraudRingRiskAppetite.Cautious
                && (summary.Grade == "C" || summary.Grade == "D" || summary.Grade == "F"))
            {
                pb.Add(new RingPlaybookAction
                {
                    Id = "SCHEDULE_FRAUD_RING_REVIEW",
                    Priority = RingPriority.P2,
                    Label = "Schedule weekly fraud-ring review",
                    Owner = "manager",
                    BlastRadius = 1,
                    Reversibility = "high",
                    Reason = "Cautious posture + non-clean grade.",
                    RingIds = rings.Select(r => r.RingId).ToList()
                });
            }

            if (_config.RiskAppetite == FraudRingRiskAppetite.Aggressive
                && pb.Any(p => p.Priority == RingPriority.P0 || p.Priority == RingPriority.P1))
            {
                pb = pb.Where(p => p.Priority != RingPriority.P3
                                && !(p.Priority == RingPriority.P2 && p.RingIds.Count <= 1))
                       .ToList();
            }

            // Dedupe by Id+RingIds set, then sort.
            var deduped = new List<RingPlaybookAction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in pb)
            {
                var k = p.Id + "|" + string.Join(",", p.RingIds.OrderBy(x => x, StringComparer.Ordinal));
                if (seen.Add(k)) deduped.Add(p);
            }
            return deduped
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .Take(_config.MaxPlaybookActions)
                .ToList();
        }

        private List<FraudRingInsight> BuildInsights(List<FraudRing> rings)
        {
            var ins = new List<FraudRingInsight>();
            if (rings.Count == 0)
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "HEALTHY_PORTFOLIO",
                    Label = "Healthy portfolio",
                    Detail = "No fraud rings detected in lookback window."
                });
                return ins;
            }

            if (rings.Any(r => r.Members.Count >= 4))
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "LARGE_RING_DETECTED",
                    Label = "Large ring detected",
                    Detail = "At least one ring has 4 or more members."
                });
            }

            var infraCodes = new HashSet<string> { "SHARED_PHONE_PREFIX", "SHARED_EMAIL_DOMAIN", "SHARED_ADDRESS" };
            if (rings.Count(r => r.Signals.Any(s => infraCodes.Contains(s.Code))) >= 2)
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "SHARED_INFRASTRUCTURE_CLUSTER",
                    Label = "Shared infrastructure cluster",
                    Detail = "Multiple rings share phone/email/address infrastructure."
                });
            }

            if (rings.Any(r => r.Signals.Any(s => s.Code == "SIMULTANEOUS_ACCOUNT_CREATION")
                            && r.Members.Any(m => m.IsNewAccount)))
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "ACCOUNT_CREATION_BURST",
                    Label = "Account creation burst",
                    Detail = "Ring members were created within the new-account window."
                });
            }

            if (rings.Count(r => r.Signals.Any(s => s.Code == "RAPID_SEQUENCE")) >= 2)
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "RAPID_SEQUENCE_PATTERN",
                    Label = "Rapid sequence pattern",
                    Detail = "Multiple rings exhibit rapid co-rental sequences."
                });
            }

            if (rings.Any(r => r.Signals.Any(s => s.Code == "DAMAGE_OR_LOSS_PATTERN")))
            {
                ins.Add(new FraudRingInsight
                {
                    Code = "COORDINATED_DAMAGE_PATTERN",
                    Label = "Coordinated damage pattern",
                    Detail = "At least one ring shows coordinated damage or loss behavior."
                });
            }

            return ins.OrderBy(i => i.Code, StringComparer.Ordinal).ToList();
        }
    }
}
