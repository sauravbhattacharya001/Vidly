using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryPromotionRepository : IPromotionRepository
    {
        private static readonly List<Promotion> _promotions = new List<Promotion>
        {
            new Promotion
            {
                Id = 1,
                Name = "Summer Blockbuster Blowout",
                Description = "Huge discounts on action and sci-fi rentals all summer long!",
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 8, 31),
                DiscountPercent = 25,
                BannerColor = "#f39c12",
                Season = PromotionSeason.Summer,
                FeaturedMovieIds = "1,3,5"
            },
            new Promotion
            {
                Id = 2,
                Name = "Holiday Movie Marathon",
                Description = "Cozy up with classic holiday films at a special discount.",
                StartDate = new DateTime(2025, 12, 15),
                EndDate = new DateTime(2026, 1, 5),
                DiscountPercent = 30,
                BannerColor = "#c0392b",
                Season = PromotionSeason.Winter,
                FeaturedMovieIds = "2,4"
            },
            new Promotion
            {
                Id = 3,
                Name = "Spring Into Cinema",
                Description = "Fresh releases and indie picks for the new season.",
                StartDate = new DateTime(2026, 3, 20),
                EndDate = new DateTime(2026, 4, 20),
                DiscountPercent = 15,
                BannerColor = "#27ae60",
                Season = PromotionSeason.Spring,
                FeaturedMovieIds = "1,2,6"
            }
        };

        private static int _nextId = 4;

        public IReadOnlyList<Promotion> GetAll() =>
            _promotions.OrderByDescending(p => p.StartDate).ToList().AsReadOnly();

        public Promotion GetById(int id) =>
            _promotions.FirstOrDefault(p => p.Id == id);

        public void Add(Promotion promotion)
        {
            if (promotion == null) throw new ArgumentNullException(nameof(promotion));
            promotion.Id = _nextId++;
            _promotions.Add(promotion);
        }

        public void Update(Promotion promotion)
        {
            if (promotion == null) throw new ArgumentNullException(nameof(promotion));
            var existing = GetById(promotion.Id);
            if (existing == null)
                throw new KeyNotFoundException($"Promotion #{promotion.Id} not found.");

            existing.Name = promotion.Name;
            existing.Description = promotion.Description;
            existing.StartDate = promotion.StartDate;
            existing.EndDate = promotion.EndDate;
            existing.DiscountPercent = promotion.DiscountPercent;
            existing.BannerColor = promotion.BannerColor;
            existing.Season = promotion.Season;
            existing.FeaturedMovieIds = promotion.FeaturedMovieIds;
        }

        public void Remove(int id)
        {
            var promo = GetById(id);
            if (promo == null)
                throw new KeyNotFoundException($"Promotion #{id} not found.");
            _promotions.Remove(promo);
        }
    }
}
