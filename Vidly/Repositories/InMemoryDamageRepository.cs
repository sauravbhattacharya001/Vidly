using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryDamageRepository : IDamageRepository
    {
        private static readonly List<DamageReport> _reports = new List<DamageReport>();
        private static int _nextId = 1;
        private static bool _seeded;

        public InMemoryDamageRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                Seed();
            }
        }

        private void Seed()
        {
            var samples = new[]
            {
                new DamageReport
                {
                    CustomerId = 1, CustomerName = "John Smith",
                    MovieId = 1, MovieTitle = "Die Hard",
                    DamageType = DamageType.ScratchedDisc, Severity = DamageSeverity.Minor,
                    Status = DamageStatus.Paid, Description = "Light circular scratches on play side",
                    AssessedFee = 2.50m, StaffNotes = "Buffed and resurfaced successfully",
                    ReportedAt = DateTime.Now.AddDays(-12), ResolvedAt = DateTime.Now.AddDays(-10)
                },
                new DamageReport
                {
                    CustomerId = 2, CustomerName = "Jane Doe",
                    MovieId = 3, MovieTitle = "Toy Story",
                    DamageType = DamageType.CrackedCase, Severity = DamageSeverity.Moderate,
                    Status = DamageStatus.Open, Description = "Front case hinge snapped, disc intact",
                    AssessedFee = 5.00m,
                    ReportedAt = DateTime.Now.AddDays(-2)
                },
                new DamageReport
                {
                    CustomerId = 3, CustomerName = "Bob Wilson",
                    MovieId = 5, MovieTitle = "The Matrix",
                    DamageType = DamageType.WaterDamage, Severity = DamageSeverity.Severe,
                    Status = DamageStatus.Assessed, Description = "Disc and insert warped from water exposure",
                    AssessedFee = 15.00m, StaffNotes = "Full replacement cost assessed",
                    ReportedAt = DateTime.Now.AddDays(-5)
                },
                new DamageReport
                {
                    CustomerId = 1, CustomerName = "John Smith",
                    MovieId = 7, MovieTitle = "Inception",
                    DamageType = DamageType.MissingDisc, Severity = DamageSeverity.Destroyed,
                    Status = DamageStatus.Disputed, Description = "Customer claims disc was missing on pickup",
                    AssessedFee = 20.00m, StaffNotes = "Checking security footage",
                    ReportedAt = DateTime.Now.AddDays(-1)
                },
            };

            foreach (var r in samples)
            {
                r.Id = _nextId++;
                _reports.Add(r);
            }
        }

        public IEnumerable<DamageReport> GetAll() => _reports.AsReadOnly();

        public DamageReport GetById(int id) => _reports.FirstOrDefault(r => r.Id == id);

        public IEnumerable<DamageReport> GetByCustomer(int customerId) =>
            _reports.Where(r => r.CustomerId == customerId);

        public IEnumerable<DamageReport> GetByMovie(int movieId) =>
            _reports.Where(r => r.MovieId == movieId);

        public IEnumerable<DamageReport> GetByStatus(DamageStatus status) =>
            _reports.Where(r => r.Status == status);

        public IEnumerable<DamageReport> GetBySeverity(DamageSeverity severity) =>
            _reports.Where(r => r.Severity == severity);

        public DamageSummary GetSummary()
        {
            return new DamageSummary
            {
                TotalReports = _reports.Count,
                OpenReports = _reports.Count(r => r.Status == DamageStatus.Open || r.Status == DamageStatus.Assessed || r.Status == DamageStatus.Disputed),
                ResolvedReports = _reports.Count(r => r.Status == DamageStatus.Paid || r.Status == DamageStatus.Waived),
                TotalFeesAssessed = _reports.Sum(r => r.AssessedFee),
                TotalFeesCollected = _reports.Where(r => r.Status == DamageStatus.Paid).Sum(r => r.AssessedFee),
                TotalFeesWaived = _reports.Where(r => r.Status == DamageStatus.Waived).Sum(r => r.AssessedFee),
                BySeverity = _reports.GroupBy(r => r.Severity).ToDictionary(g => g.Key, g => g.Count()),
                ByType = _reports.GroupBy(r => r.DamageType).ToDictionary(g => g.Key, g => g.Count()),
            };
        }

        public void Add(DamageReport report)
        {
            report.Id = _nextId++;
            report.ReportedAt = DateTime.Now;
            _reports.Add(report);
        }

        public void Update(DamageReport report)
        {
            var existing = GetById(report.Id);
            if (existing == null) return;

            existing.Status = report.Status;
            existing.AssessedFee = report.AssessedFee;
            existing.StaffNotes = report.StaffNotes;
            existing.Severity = report.Severity;
            if (report.Status == DamageStatus.Paid || report.Status == DamageStatus.Waived)
                existing.ResolvedAt = DateTime.Now;
        }
    }
}
