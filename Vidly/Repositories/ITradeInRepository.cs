using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface ITradeInRepository
    {
        IEnumerable<TradeIn> GetAll();
        TradeIn GetById(int id);
        IEnumerable<TradeIn> GetByCustomer(int customerId);
        IEnumerable<TradeIn> GetPending();
        void Add(TradeIn tradeIn);
        void Update(TradeIn tradeIn);
        void Remove(int id);
        TradeInStats GetStats();
    }

    public class TradeInStats
    {
        public int TotalTradeIns { get; set; }
        public int PendingCount { get; set; }
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalCreditsAwarded { get; set; }
        public Dictionary<string, int> ByFormat { get; set; } = new Dictionary<string, int>();
    }
}
