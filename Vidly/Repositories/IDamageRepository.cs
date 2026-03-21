using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IDamageRepository
    {
        IEnumerable<DamageReport> GetAll();
        DamageReport GetById(int id);
        IEnumerable<DamageReport> GetByCustomer(int customerId);
        IEnumerable<DamageReport> GetByMovie(int movieId);
        IEnumerable<DamageReport> GetByStatus(DamageStatus status);
        IEnumerable<DamageReport> GetBySeverity(DamageSeverity severity);
        DamageSummary GetSummary();
        void Add(DamageReport report);
        void Update(DamageReport report);
    }
}
