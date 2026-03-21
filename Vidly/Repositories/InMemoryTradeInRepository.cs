using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryTradeInRepository : ITradeInRepository
    {
        private static readonly List<TradeIn> _tradeIns = new List<TradeIn>();
        private static int _nextId = 1;
        private static bool _seeded;

        // Credit values by format + condition
        private static readonly Dictionary<TradeInFormat, Dictionary<TradeInCondition, decimal>> _creditTable =
            new Dictionary<TradeInFormat, Dictionary<TradeInCondition, decimal>>
            {
                [TradeInFormat.UHD4K] = new Dictionary<TradeInCondition, decimal>
                    { [TradeInCondition.LikeNew] = 8m, [TradeInCondition.Good] = 6m, [TradeInCondition.Fair] = 4m, [TradeInCondition.Poor] = 1.5m },
                [TradeInFormat.BluRay] = new Dictionary<TradeInCondition, decimal>
                    { [TradeInCondition.LikeNew] = 5m, [TradeInCondition.Good] = 3.5m, [TradeInCondition.Fair] = 2m, [TradeInCondition.Poor] = 1m },
                [TradeInFormat.DVD] = new Dictionary<TradeInCondition, decimal>
                    { [TradeInCondition.LikeNew] = 3m, [TradeInCondition.Good] = 2m, [TradeInCondition.Fair] = 1m, [TradeInCondition.Poor] = 0.5m },
                [TradeInFormat.VHS] = new Dictionary<TradeInCondition, decimal>
                    { [TradeInCondition.LikeNew] = 1.5m, [TradeInCondition.Good] = 1m, [TradeInCondition.Fair] = 0.5m, [TradeInCondition.Poor] = 0.25m },
            };

        public InMemoryTradeInRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                SeedData();
            }
        }

        private void SeedData()
        {
            var items = new[]
            {
                new TradeIn { CustomerId = 1, MovieTitle = "The Godfather", Format = TradeInFormat.BluRay, Condition = TradeInCondition.Good, CreditsAwarded = 3.5m, TradeInDate = DateTime.Now.AddDays(-10), Status = TradeInStatus.Accepted },
                new TradeIn { CustomerId = 2, MovieTitle = "Jurassic Park", Format = TradeInFormat.DVD, Condition = TradeInCondition.Fair, CreditsAwarded = 1m, TradeInDate = DateTime.Now.AddDays(-5), Status = TradeInStatus.Accepted },
                new TradeIn { CustomerId = 3, MovieTitle = "Blade Runner 2049", Format = TradeInFormat.UHD4K, Condition = TradeInCondition.LikeNew, CreditsAwarded = 0m, TradeInDate = DateTime.Now.AddDays(-1), Status = TradeInStatus.Pending },
                new TradeIn { CustomerId = 1, MovieTitle = "Home Alone", Format = TradeInFormat.VHS, Condition = TradeInCondition.Poor, CreditsAwarded = 0.25m, TradeInDate = DateTime.Now.AddDays(-20), Status = TradeInStatus.Accepted },
                new TradeIn { CustomerId = 2, MovieTitle = "Scratched Disc", Format = TradeInFormat.DVD, Condition = TradeInCondition.Poor, CreditsAwarded = 0m, TradeInDate = DateTime.Now.AddDays(-3), Status = TradeInStatus.Rejected, Notes = "Disc unreadable" },
            };

            foreach (var item in items)
            {
                item.Id = _nextId++;
                _tradeIns.Add(item);
            }
        }

        public static decimal CalculateCredits(TradeInFormat format, TradeInCondition condition)
        {
            if (_creditTable.TryGetValue(format, out var conditions) &&
                conditions.TryGetValue(condition, out var credits))
                return credits;
            return 0m;
        }

        public IEnumerable<TradeIn> GetAll() =>
            _tradeIns.OrderByDescending(t => t.TradeInDate).ToList();

        public TradeIn GetById(int id) =>
            _tradeIns.FirstOrDefault(t => t.Id == id);

        public IEnumerable<TradeIn> GetByCustomer(int customerId) =>
            _tradeIns.Where(t => t.CustomerId == customerId)
                      .OrderByDescending(t => t.TradeInDate).ToList();

        public IEnumerable<TradeIn> GetPending() =>
            _tradeIns.Where(t => t.Status == TradeInStatus.Pending)
                      .OrderBy(t => t.TradeInDate).ToList();

        public void Add(TradeIn tradeIn)
        {
            tradeIn.Id = _nextId++;
            tradeIn.TradeInDate = DateTime.Now;
            tradeIn.Status = TradeInStatus.Pending;
            tradeIn.CreditsAwarded = CalculateCredits(tradeIn.Format, tradeIn.Condition);
            _tradeIns.Add(tradeIn);
        }

        public void Update(TradeIn tradeIn)
        {
            var idx = _tradeIns.FindIndex(t => t.Id == tradeIn.Id);
            if (idx >= 0) _tradeIns[idx] = tradeIn;
        }

        public void Remove(int id)
        {
            _tradeIns.RemoveAll(t => t.Id == id);
        }

        public TradeInStats GetStats()
        {
            return new TradeInStats
            {
                TotalTradeIns = _tradeIns.Count,
                PendingCount = _tradeIns.Count(t => t.Status == TradeInStatus.Pending),
                AcceptedCount = _tradeIns.Count(t => t.Status == TradeInStatus.Accepted),
                RejectedCount = _tradeIns.Count(t => t.Status == TradeInStatus.Rejected),
                TotalCreditsAwarded = _tradeIns.Where(t => t.Status == TradeInStatus.Accepted).Sum(t => t.CreditsAwarded),
                ByFormat = _tradeIns.GroupBy(t => t.Format.ToString())
                                     .ToDictionary(g => g.Key, g => g.Count())
            };
        }
    }
}
