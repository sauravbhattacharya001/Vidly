using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Shared rental analytics utilities used by ChurnPredictorService,
    /// WinBackService, and other customer-analysis services.
    /// Eliminates duplicated rental-lookup, genre-distribution, gap-computation,
    /// and entropy calculations that were independently implemented.
    /// </summary>
    public static class CustomerRentalAnalytics
    {
        /// <summary>
        /// Groups rentals by customer ID into a dictionary for O(1) per-customer lookup.
        /// Replaces identical implementations in WinBackService.BuildRentalLookup
        /// and ChurnPredictorService.AnalyzeAll's inline GroupBy.
        /// </summary>
        public static Dictionary<int, List<Rental>> BuildRentalsByCustomer(IEnumerable<Rental> rentals)
        {
            var lookup = new Dictionary<int, List<Rental>>();
            foreach (var r in rentals)
            {
                List<Rental> list;
                if (!lookup.TryGetValue(r.CustomerId, out list))
                {
                    list = new List<Rental>();
                    lookup[r.CustomerId] = list;
                }
                list.Add(r);
            }
            return lookup;
        }

        /// <summary>
        /// Computes the genre distribution for a customer's rentals.
        /// Returns genre name → count mapping.
        /// </summary>
        public static Dictionary<string, int> ComputeGenreDistribution(
            IEnumerable<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var counts = new Dictionary<string, int>();
            foreach (var r in rentals)
            {
                Movie movie;
                var genre = movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue
                    ? movie.Genre.Value.ToString()
                    : "Unknown";
                if (!counts.ContainsKey(genre))
                    counts[genre] = 0;
                counts[genre]++;
            }
            return counts;
        }

        /// <summary>
        /// Computes the number of distinct genres rented by a customer.
        /// Single-pass with O(1) HashSet lookups instead of chained LINQ
        /// (Where → Select → Distinct → Count was 4 iterations + allocations).
        /// </summary>
        public static int CountDistinctGenres(
            IEnumerable<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var genres = new HashSet<Genre>();
            foreach (var r in rentals)
            {
                Movie movie;
                if (movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue)
                    genres.Add(movie.Genre.Value);
            }
            return genres.Count;
        }

        /// <summary>
        /// Computes consecutive rental gaps (days between successive rentals).
        /// Shared by ChurnPredictorService and potentially other trend-analysis code.
        /// </summary>
        public static List<double> ComputeRentalGaps(IReadOnlyList<Rental> sortedRentals)
        {
            if (sortedRentals.Count < 2) return new List<double>();
            var gaps = new List<double>(sortedRentals.Count - 1);
            for (int i = 1; i < sortedRentals.Count; i++)
                gaps.Add((sortedRentals[i].RentalDate - sortedRentals[i - 1].RentalDate).TotalDays);
            return gaps;
        }

        /// <summary>
        /// Computes the average gap in days between consecutive rentals.
        /// </summary>
        public static double AverageRentalGap(IReadOnlyList<Rental> sortedRentals)
        {
            var gaps = ComputeRentalGaps(sortedRentals);
            return gaps.Count > 0 ? gaps.Average() : 0;
        }

        /// <summary>
        /// Computes frequency trend: positive means gaps are growing (declining engagement).
        /// Compares first-half average gap to second-half average gap.
        /// </summary>
        public static double FrequencyTrend(IReadOnlyList<Rental> sortedRentals, int minRentalsForTrend = 4)
        {
            if (sortedRentals.Count < minRentalsForTrend) return 0;

            var gaps = ComputeRentalGaps(sortedRentals);
            if (gaps.Count < 2) return 0;

            var mid = gaps.Count / 2;
            var firstHalf = gaps.Take(mid).Average();
            var secondHalf = gaps.Skip(mid).Average();

            if (firstHalf == 0) return 0;
            return (secondHalf - firstHalf) / firstHalf;
        }

        /// <summary>
        /// Computes the late return rate: fraction of returned rentals that were late.
        /// Single pass — counts returned and late simultaneously instead of
        /// filtering to a list then scanning again.
        /// </summary>
        public static double LateReturnRate(IEnumerable<Rental> rentals)
        {
            int returnedCount = 0;
            int lateCount = 0;
            foreach (var r in rentals)
            {
                if (r.ReturnDate.HasValue)
                {
                    returnedCount++;
                    if (r.ReturnDate.Value > r.DueDate)
                        lateCount++;
                }
            }
            return returnedCount == 0 ? 0 : (double)lateCount / returnedCount;
        }

        /// <summary>
        /// Normalized Shannon entropy for a distribution.
        /// Returns 0..1 where 0 = single category, 1 = perfectly uniform.
        /// </summary>
        public static double ShannonEntropy(Dictionary<string, int> counts)
        {
            return ShannonEntropy(counts.Values);
        }

        /// <summary>
        /// Normalized Shannon entropy for a collection of counts.
        /// Returns 0..1 where 0 = single category, 1 = perfectly uniform.
        /// Shared overload used by CustomerWrappedService and others that
        /// have raw count lists rather than string-keyed dictionaries.
        /// </summary>
        public static double ShannonEntropy(IEnumerable<int> values)
        {
            var countList = values as ICollection<int> ?? values.ToList();
            if (countList.Count <= 1) return 0;

            var total = 0;
            foreach (var c in countList) total += c;
            if (total == 0) return 0;

            var entropy = 0.0;
            foreach (var count in countList)
            {
                if (count <= 0) continue;
                var p = (double)count / total;
                entropy -= p * Math.Log(p);
            }

            return entropy / Math.Log(countList.Count);
        }
    }
}
