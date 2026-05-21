using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class WaitlistConversionAdvisorServiceTests
    {
        private static readonly DateTime Today = new DateTime(2026, 5, 20);
        private static readonly DateTime Now = new DateTime(2026, 5, 20, 12, 0, 0);

        private static WaitlistConversionAdvisorService BuildService(
            out InMemoryTestWaitlistRepo waitlist,
            out InMemoryTestMovieRepo2 movies)
        {
            waitlist = new InMemoryTestWaitlistRepo();
            movies = new InMemoryTestMovieRepo2();
            return new WaitlistConversionAdvisorService(waitlist, movies, new FakeClock2(Today, Now));
        }

        private static Movie M(int id, string name, decimal? rate = null) =>
            new Movie { Id = id, Name = name, DailyRate = rate };

        private static WaitlistEntry E(
            int id, int movieId, int customerId,
            WaitlistStatus status,
            int joinedDaysAgo,
            double? expiresInHours = null,
            WaitlistPriority priority = WaitlistPriority.Normal,
            int position = 1,
            int? notifiedDaysAgo = null)
        {
            return new WaitlistEntry
            {
                Id = id,
                MovieId = movieId,
                CustomerId = customerId,
                Status = status,
                JoinedAt = Now.AddDays(-joinedDaysAgo),
                ExpiresAt = expiresInHours.HasValue ? Now.AddHours(expiresInHours.Value) : (DateTime?)null,
                NotifiedAt = notifiedDaysAgo.HasValue ? Now.AddDays(-notifiedDaysAgo.Value) : (DateTime?)null,
                Priority = priority,
                Position = position,
                MovieName = "Movie#" + movieId,
                CustomerName = "Cust#" + customerId
            };
        }

        // ── Tests ────────────────────────────────────────────────

        [TestMethod]
        public void NoWaitlists_HealthyReport()
        {
            var svc = BuildService(out _, out _);
            var report = svc.GenerateReport();

            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual(100, report.Summary.OverallScore);
            Assert.AreEqual('A', report.Summary.Grade);
            CollectionAssert.Contains(report.Summary.Insights, "INSUFFICIENT_DATA");
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("waitlist_healthy", report.Playbook[0].Id);
        }

        [TestMethod]
        public void ExpiredNotification_GeneratesP0AndForcesGradeF()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(1, "The Expired"));
            // Notified, expired 6h ago.
            w.Add(E(1, movieId: 1, customerId: 10, WaitlistStatus.Notified,
                    joinedDaysAgo: 10, expiresInHours: -6, notifiedDaysAgo: 1));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            var c = report.Cases[0];
            Assert.AreEqual(WaitlistActionPriority.P0, c.Priority);
            Assert.AreEqual(WaitlistVerdict.ExpiredNotified, c.Verdict);
            Assert.IsTrue(c.Risk >= 90);
            Assert.AreEqual(1, c.ExpiredNotifiedCount);
            Assert.IsTrue(report.Summary.OverallScore <= 55);
            Assert.AreEqual('F', report.Summary.Grade);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "retire_expired_notifications"));
            CollectionAssert.Contains(report.Summary.Insights, "EXPIRED_NOTIFICATIONS_PRESENT");
        }

        [TestMethod]
        public void WindowClosingSoon_GeneratesP1()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(2, "Closing Soon"));
            w.Add(E(1, 2, 11, WaitlistStatus.Notified,
                    joinedDaysAgo: 4, expiresInHours: 12, notifiedDaysAgo: 1));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(WaitlistActionPriority.P1, c.Priority);
            Assert.AreEqual(WaitlistVerdict.WindowClosing, c.Verdict);
            Assert.AreEqual(1, c.ClosingWindowCount);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "nudge_closing_windows"));
        }

        [TestMethod]
        public void UrgentWaiterPastSla_FlagsUrgentNeglected()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(3, "Urgent Title"));
            w.Add(E(1, 3, 12, WaitlistStatus.Waiting,
                    joinedDaysAgo: 5, priority: WaitlistPriority.Urgent));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.IsTrue(c.HasUrgentNeglected);
            Assert.IsTrue(c.Risk >= 75);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "fast_track_urgent_waiters"));
            CollectionAssert.Contains(report.Summary.Insights, "URGENT_NEGLECTED_PRESENT");
        }

        [TestMethod]
        public void LongWaiter_FlagsLongWaiter()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(4, "Patience Test"));
            w.Add(E(1, 4, 13, WaitlistStatus.Waiting, joinedDaysAgo: 30));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(WaitlistVerdict.LongWaiter, c.Verdict);
            Assert.IsTrue(c.MaxWaiterDays >= 30);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "outreach_long_waiters"));
            CollectionAssert.Contains(report.Summary.Insights, "LONG_TAIL_WAITERS");
        }

        [TestMethod]
        public void ChronicAbandonment_FlagsChronic()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(5, "Abandon-prone"));
            // Recent 5 entries: 4 expired, 1 active waiting -> abandon% = 80
            for (int i = 1; i <= 4; i++)
                w.Add(E(i, 5, 100 + i, WaitlistStatus.Expired, joinedDaysAgo: 8 - i));
            w.Add(E(5, 5, 200, WaitlistStatus.Waiting, joinedDaysAgo: 1));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.IsTrue(c.AbandonRatePct >= 50);
            Assert.AreEqual(WaitlistVerdict.ChronicAbandonment, c.Verdict);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "audit_chronic_abandonment"));
            CollectionAssert.Contains(report.Summary.Insights, "CHRONIC_ABANDONMENT");
        }

        [TestMethod]
        public void DeepBacklog_FlagsDeepBacklog()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(6, "Hot Title"));
            for (int i = 1; i <= 8; i++)
                w.Add(E(i, 6, 300 + i, WaitlistStatus.Waiting, joinedDaysAgo: 1, position: i));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.IsTrue(c.ActiveWaitingCount >= WaitlistConversionAdvisorService.DeepBacklogMin);
            Assert.IsTrue(c.Verdict == WaitlistVerdict.DeepBacklog || c.Verdict == WaitlistVerdict.LongWaiter
                          || c.Verdict == WaitlistVerdict.ChronicAbandonment);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "expand_copy_supply"));
            CollectionAssert.Contains(report.Summary.Insights, "DEEP_BACKLOG");
        }

        [TestMethod]
        public void HealthyShortList_ListOk()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(7, "Easy Going"));
            w.Add(E(1, 7, 14, WaitlistStatus.Waiting, joinedDaysAgo: 2));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(WaitlistVerdict.ListOk, c.Verdict);
            Assert.AreEqual(WaitlistActionPriority.P3, c.Priority);
            Assert.IsTrue(c.Risk <= 20);
        }

        [TestMethod]
        public void CautiousAppetite_BoostsPriorityAndRisk()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(8, "Subtle"));
            // Long waiter at P2 normally.
            w.Add(E(1, 8, 15, WaitlistStatus.Waiting, joinedDaysAgo: 25));

            var bal = svc.GenerateReport(WaitlistConversionAppetite.Balanced).Cases.Single();
            var cau = svc.GenerateReport(WaitlistConversionAppetite.Cautious).Cases.Single();
            Assert.IsTrue((int)cau.Priority <= (int)bal.Priority);
            Assert.IsTrue(cau.Risk >= bal.Risk);
        }

        [TestMethod]
        public void HighValueMovie_BoostsRisk()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(9, "Premium", rate: 5.50m));
            w.Add(E(1, 9, 16, WaitlistStatus.Waiting, joinedDaysAgo: 25));

            var svc2 = BuildService(out var w2, out var movies2);
            movies2.Add(M(9, "Cheap", rate: 1.50m));
            w2.Add(E(1, 9, 16, WaitlistStatus.Waiting, joinedDaysAgo: 25));

            var hi = svc.GenerateReport().Cases.Single();
            var lo = svc2.GenerateReport().Cases.Single();
            Assert.IsTrue(hi.Risk >= lo.Risk);
        }

        [TestMethod]
        public void NotifiedEntry_IgnoresActiveOnlyAggregates()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(10, "Just Notified"));
            w.Add(E(1, 10, 17, WaitlistStatus.Notified,
                    joinedDaysAgo: 2, expiresInHours: 48, notifiedDaysAgo: 0));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(1, c.NotifiedCount);
            Assert.AreEqual(0, c.ActiveWaitingCount);
            Assert.AreEqual(0, c.ExpiredNotifiedCount);
        }

        [TestMethod]
        public void OnlyTerminalEntries_NoCase()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(11, "All Done"));
            w.Add(E(1, 11, 18, WaitlistStatus.Fulfilled, joinedDaysAgo: 10, notifiedDaysAgo: 8));
            w.Add(E(2, 11, 19, WaitlistStatus.Cancelled, joinedDaysAgo: 9));
            w.Add(E(3, 11, 20, WaitlistStatus.Expired, joinedDaysAgo: 7));

            var report = svc.GenerateReport();
            Assert.AreEqual(0, report.Cases.Count);
        }

        [TestMethod]
        public void MultipleMovies_SortedByRiskDescThenIdAsc()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(20, "Low Risk"));
            movies.Add(M(21, "Tie Risk A"));
            movies.Add(M(22, "Tie Risk B"));
            movies.Add(M(23, "High Risk"));

            w.Add(E(1, 20, 30, WaitlistStatus.Waiting, joinedDaysAgo: 1));
            w.Add(E(2, 21, 31, WaitlistStatus.Waiting, joinedDaysAgo: 2));
            w.Add(E(3, 22, 32, WaitlistStatus.Waiting, joinedDaysAgo: 2));
            // expired notification -> highest risk
            w.Add(E(4, 23, 33, WaitlistStatus.Notified,
                    joinedDaysAgo: 5, expiresInHours: -10, notifiedDaysAgo: 1));

            var report = svc.GenerateReport();
            Assert.AreEqual(23, report.Cases[0].MovieId);
            // ids 21 and 22 should appear in ascending order among ties.
            var idxA = report.Cases.FindIndex(c => c.MovieId == 21);
            var idxB = report.Cases.FindIndex(c => c.MovieId == 22);
            Assert.IsTrue(idxA < idxB);
        }

        [TestMethod]
        public void GenerateReport_IsDeterministic()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(30, "Repeatable"));
            w.Add(E(1, 30, 40, WaitlistStatus.Notified,
                    joinedDaysAgo: 5, expiresInHours: -2, notifiedDaysAgo: 1));
            w.Add(E(2, 30, 41, WaitlistStatus.Waiting, joinedDaysAgo: 22));

            var a = svc.ToJson(svc.GenerateReport());
            var b = svc.ToJson(svc.GenerateReport());
            Assert.AreEqual(a, b);
            StringAssert.Contains(a, "\"asOfDate\"");
            StringAssert.Contains(a, "\"cases\"");
            StringAssert.Contains(a, "\"playbook\"");
            StringAssert.Contains(a, "\"summary\"");
        }

        [TestMethod]
        public void ToMarkdown_HasCanonicalSections()
        {
            var svc = BuildService(out var w, out var movies);
            movies.Add(M(40, "Doc Test"));
            w.Add(E(1, 40, 50, WaitlistStatus.Waiting, joinedDaysAgo: 1));

            var md = svc.ToMarkdown(svc.GenerateReport());
            StringAssert.Contains(md, "## Cases");
            StringAssert.Contains(md, "## Playbook");
            StringAssert.Contains(md, "Waitlist Conversion Advisor");
        }

        [TestMethod]
        public void ToText_RendersHeadline()
        {
            var svc = BuildService(out _, out _);
            var txt = svc.ToText(svc.GenerateReport());
            StringAssert.Contains(txt, "VERDICT:");
            StringAssert.Contains(txt, "waitlist_healthy");
        }

        [TestMethod]
        public void NullConstructorArgs_Throw()
        {
            var clock = new FakeClock2(Today, Now);
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WaitlistConversionAdvisorService(null, new InMemoryTestMovieRepo2(), clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WaitlistConversionAdvisorService(new InMemoryTestWaitlistRepo(), null, clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WaitlistConversionAdvisorService(new InMemoryTestWaitlistRepo(), new InMemoryTestMovieRepo2(), null));
        }

        [TestMethod]
        public void NullReportArgs_Throw()
        {
            var svc = BuildService(out _, out _);
            Assert.ThrowsException<ArgumentNullException>(() => svc.ToJson(null));
            Assert.ThrowsException<ArgumentNullException>(() => svc.ToMarkdown(null));
            Assert.ThrowsException<ArgumentNullException>(() => svc.ToText(null));
        }

        // ── Test doubles ─────────────────────────────────────────

        private class FakeClock2 : IClock
        {
            private readonly DateTime _today;
            private readonly DateTime _now;
            public FakeClock2(DateTime today, DateTime now) { _today = today; _now = now; }
            public DateTime Today => _today;
            public DateTime Now => _now;
        }

        private class InMemoryTestWaitlistRepo : IWaitlistRepository
        {
            private readonly List<WaitlistEntry> _data = new List<WaitlistEntry>();
            public void Add(WaitlistEntry entry) => _data.Add(entry);
            public void Update(WaitlistEntry entry) { }
            public void Remove(int id) => _data.RemoveAll(e => e.Id == id);
            public IEnumerable<WaitlistEntry> GetAll() => _data.ToList();
            public WaitlistEntry GetById(int id) => _data.FirstOrDefault(e => e.Id == id);
            public IEnumerable<WaitlistEntry> GetByCustomer(int customerId) =>
                _data.Where(e => e.CustomerId == customerId).ToList();
            public IEnumerable<WaitlistEntry> GetByMovie(int movieId) =>
                _data.Where(e => e.MovieId == movieId).ToList();
            public IEnumerable<WaitlistEntry> GetActiveByMovie(int movieId) =>
                _data.Where(e => e.MovieId == movieId && e.Status == WaitlistStatus.Waiting).ToList();
            public WaitlistEntry FindExisting(int customerId, int movieId) =>
                _data.FirstOrDefault(e => e.CustomerId == customerId && e.MovieId == movieId &&
                                          (e.Status == WaitlistStatus.Waiting ||
                                           e.Status == WaitlistStatus.Notified));
            public WaitlistStats GetStats() => new WaitlistStats { TotalWaiting = _data.Count };
        }

        private class InMemoryTestMovieRepo2 : IMovieRepository
        {
            private readonly List<Movie> _data = new List<Movie>();
            public void Add(Movie entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _data.AsReadOnly();
            public Movie GetById(int id) => _data.FirstOrDefault(m => m.Id == id);
            public void Update(Movie entity) { }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _data.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _data.AsReadOnly();
        }
    }
}
