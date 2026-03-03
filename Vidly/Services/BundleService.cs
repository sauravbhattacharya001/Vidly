using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages bundle deals and calculates discounts for multi-movie rentals.
    /// </summary>
    public class BundleService
    {
        private readonly List<BundleDeal> _bundles;
        private readonly object _lock = new object();
        private int _nextId = 1;

        public BundleService()
        {
            _bundles = new List<BundleDeal>();
            SeedDefaults();
        }

        private void SeedDefaults()
        {
            Add(new BundleDeal
            {
                Name = "3 for 2",
                Description = "Rent 3 movies and only pay for the 2 most expensive ones!",
                MinMovies = 3,
                MaxMovies = 3,
                DiscountType = BundleDiscountType.FreeMovies,
                DiscountValue = 1,
                IsActive = true
            });

            Add(new BundleDeal
            {
                Name = "Weekend Binge",
                Description = "Rent 5 or more movies and get 25% off the total!",
                MinMovies = 5,
                MaxMovies = 0,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 25,
                IsActive = true
            });

            Add(new BundleDeal
            {
                Name = "Double Feature",
                Description = "Rent 2 movies and get $2 off!",
                MinMovies = 2,
                MaxMovies = 2,
                DiscountType = BundleDiscountType.FixedAmount,
                DiscountValue = 2.00m,
                IsActive = true
            });

            Add(new BundleDeal
            {
                Name = "Action Pack",
                Description = "Rent 4+ action movies and get 30% off!",
                MinMovies = 4,
                MaxMovies = 0,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 30,
                RequiredGenre = Genre.Action,
                IsActive = true
            });
        }

        public IReadOnlyList<BundleDeal> GetAll()
        {
            lock (_lock) { return _bundles.ToList(); }
        }

        public IReadOnlyList<BundleDeal> GetActive()
        {
            lock (_lock) { return _bundles.Where(b => b.IsCurrentlyValid).ToList(); }
        }

        public BundleDeal GetById(int id)
        {
            lock (_lock) { return _bundles.FirstOrDefault(b => b.Id == id); }
        }

        public BundleDeal Add(BundleDeal bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (string.IsNullOrWhiteSpace(bundle.Name))
                throw new ArgumentException("Bundle name is required.");
            if (bundle.MinMovies < 2)
                throw new ArgumentException("Minimum movies must be at least 2.");
            if (bundle.DiscountValue <= 0)
                throw new ArgumentException("Discount value must be positive.");
            if (bundle.DiscountType == BundleDiscountType.Percentage && bundle.DiscountValue > 100)
                throw new ArgumentException("Percentage discount cannot exceed 100%.");
            if (bundle.DiscountType == BundleDiscountType.FreeMovies && bundle.DiscountValue >= bundle.MinMovies)
                throw new ArgumentException("Free movies must be less than minimum movies.");

            lock (_lock)
            {
                bundle.Id = _nextId++;
                _bundles.Add(bundle);
                return bundle;
            }
        }

        public BundleDeal Update(BundleDeal bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));

            lock (_lock)
            {
                var existing = _bundles.FirstOrDefault(b => b.Id == bundle.Id);
                if (existing == null)
                    throw new KeyNotFoundException($"Bundle with ID {bundle.Id} not found.");

                existing.Name = bundle.Name;
                existing.Description = bundle.Description;
                existing.MinMovies = bundle.MinMovies;
                existing.MaxMovies = bundle.MaxMovies;
                existing.DiscountType = bundle.DiscountType;
                existing.DiscountValue = bundle.DiscountValue;
                existing.RequiredGenre = bundle.RequiredGenre;
                existing.IsActive = bundle.IsActive;
                existing.StartDate = bundle.StartDate;
                existing.EndDate = bundle.EndDate;

                return existing;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                var bundle = _bundles.FirstOrDefault(b => b.Id == id);
                if (bundle == null)
                    throw new KeyNotFoundException($"Bundle with ID {id} not found.");
                _bundles.Remove(bundle);
            }
        }

        /// <summary>
        /// Finds the best applicable bundle for a set of movies and returns the discount result.
        /// </summary>
        public BundleApplyResult FindBestBundle(
            IReadOnlyList<Movie> movies,
            IDictionary<int, decimal> dailyRates,
            int rentalDays)
        {
            if (movies == null || movies.Count == 0)
                return new BundleApplyResult { OriginalTotal = 0, DiscountAmount = 0 };

            var moviePrices = movies.Select(m => new MoviePrice
            {
                MovieId = m.Id,
                MovieName = m.Name,
                DailyRate = dailyRates.ContainsKey(m.Id) ? dailyRates[m.Id] : 0
            }).ToList();

            var originalTotal = moviePrices.Sum(p => p.DailyRate * rentalDays);

            var noDiscount = new BundleApplyResult
            {
                OriginalTotal = originalTotal,
                DiscountAmount = 0,
                MoviePrices = moviePrices
            };

            var activeBundles = GetActive();
            if (activeBundles.Count == 0) return noDiscount;

            BundleApplyResult bestResult = null;
            decimal bestDiscount = 0;

            foreach (var bundle in activeBundles)
            {
                var result = TryApplyBundle(bundle, movies, moviePrices, rentalDays, originalTotal);
                if (result != null && result.DiscountAmount > bestDiscount)
                {
                    bestResult = result;
                    bestDiscount = result.DiscountAmount;
                }
            }

            return bestResult ?? noDiscount;
        }

        private BundleApplyResult TryApplyBundle(
            BundleDeal bundle,
            IReadOnlyList<Movie> movies,
            List<MoviePrice> moviePrices,
            int rentalDays,
            decimal originalTotal)
        {
            var qualifying = bundle.RequiredGenre.HasValue
                ? movies.Where(m => m.Genre == bundle.RequiredGenre.Value).ToList()
                : movies.ToList();

            if (qualifying.Count < bundle.MinMovies) return null;

            var count = bundle.MaxMovies > 0
                ? Math.Min(qualifying.Count, bundle.MaxMovies)
                : qualifying.Count;

            if (count < bundle.MinMovies) return null;

            var qualifyingPrices = moviePrices
                .Where(p => qualifying.Any(m => m.Id == p.MovieId))
                .OrderBy(p => p.DailyRate)
                .Take(count)
                .ToList();

            decimal discount;

            switch (bundle.DiscountType)
            {
                case BundleDiscountType.Percentage:
                    var qualifyingTotal = qualifyingPrices.Sum(p => p.DailyRate * rentalDays);
                    discount = Math.Round(qualifyingTotal * bundle.DiscountValue / 100m, 2);
                    break;

                case BundleDiscountType.FixedAmount:
                    discount = Math.Min(bundle.DiscountValue, originalTotal);
                    break;

                case BundleDiscountType.FreeMovies:
                    var freeCount = (int)Math.Min(bundle.DiscountValue, qualifyingPrices.Count - 1);
                    var freePrices = qualifyingPrices.Take(freeCount).ToList();
                    discount = freePrices.Sum(p => p.DailyRate * rentalDays);

                    var updatedPrices = moviePrices.Select(p => new MoviePrice
                    {
                        MovieId = p.MovieId,
                        MovieName = p.MovieName,
                        DailyRate = p.DailyRate,
                        IsFree = freePrices.Any(f => f.MovieId == p.MovieId)
                    }).ToList();

                    return new BundleApplyResult
                    {
                        Bundle = bundle,
                        OriginalTotal = originalTotal,
                        DiscountAmount = Math.Round(discount, 2),
                        MoviePrices = updatedPrices
                    };

                default:
                    return null;
            }

            return new BundleApplyResult
            {
                Bundle = bundle,
                OriginalTotal = originalTotal,
                DiscountAmount = Math.Round(discount, 2),
                MoviePrices = moviePrices
            };
        }

        public void RecordUsage(int bundleId)
        {
            lock (_lock)
            {
                var bundle = _bundles.FirstOrDefault(b => b.Id == bundleId);
                if (bundle != null) bundle.TimesUsed++;
            }
        }

        public BundleStats GetStats()
        {
            lock (_lock)
            {
                return new BundleStats
                {
                    TotalBundles = _bundles.Count,
                    ActiveBundles = _bundles.Count(b => b.IsCurrentlyValid),
                    TotalUsage = _bundles.Sum(b => b.TimesUsed),
                    MostPopularBundle = _bundles.OrderByDescending(b => b.TimesUsed).FirstOrDefault()?.Name,
                    BundleUsage = _bundles
                        .OrderByDescending(b => b.TimesUsed)
                        .Select(b => new BundleUsageEntry { Name = b.Name, TimesUsed = b.TimesUsed })
                        .ToList()
                };
            }
        }
    }

    public class BundleStats
    {
        public int TotalBundles { get; set; }
        public int ActiveBundles { get; set; }
        public int TotalUsage { get; set; }
        public string MostPopularBundle { get; set; }
        public IReadOnlyList<BundleUsageEntry> BundleUsage { get; set; }
    }

    public class BundleUsageEntry
    {
        public string Name { get; set; }
        public int TimesUsed { get; set; }
    }
}
