using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryGiftRegistryRepository : IGiftRegistryRepository
    {
        private static readonly List<GiftRegistry> _registries = new List<GiftRegistry>();
        private static int _nextId = 1;
        private static int _nextItemId = 1;

        static InMemoryGiftRegistryRepository()
        {
            // Seed sample data
            var registry1 = new GiftRegistry
            {
                Id = _nextId++,
                CustomerId = 1,
                Name = "John's Birthday Movie Marathon",
                Description = "Help me build the ultimate birthday movie night!",
                Occasion = GiftRegistryOccasion.Birthday,
                EventDate = DateTime.Now.AddDays(30),
                ShareCode = "JOHN-BDAY-2026",
                Items = new List<GiftRegistryItem>
                {
                    new GiftRegistryItem { Id = _nextItemId++, RegistryId = 1, MovieId = 1, MovieName = "Die Hard", Note = "My all-time favorite!" },
                    new GiftRegistryItem { Id = _nextItemId++, RegistryId = 1, MovieId = 2, MovieName = "The Dark Knight", Note = "Haven't seen it yet" },
                    new GiftRegistryItem { Id = _nextItemId++, RegistryId = 1, MovieId = 3, MovieName = "Inception", Status = GiftRegistryItemStatus.Fulfilled, FulfilledBy = "Jane", FulfilledAt = DateTime.Now.AddDays(-2) }
                }
            };

            var registry2 = new GiftRegistry
            {
                Id = _nextId++,
                CustomerId = 2,
                Name = "Holiday Movie Collection",
                Description = "Classic holiday films for the season",
                Occasion = GiftRegistryOccasion.Holiday,
                EventDate = DateTime.Now.AddDays(60),
                ShareCode = "HOLIDAY-FUN-26",
                Items = new List<GiftRegistryItem>
                {
                    new GiftRegistryItem { Id = _nextItemId++, RegistryId = 2, MovieId = 4, MovieName = "Home Alone", Note = "A must for the holidays" },
                    new GiftRegistryItem { Id = _nextItemId++, RegistryId = 2, MovieId = 5, MovieName = "It's a Wonderful Life" }
                }
            };

            _registries.Add(registry1);
            _registries.Add(registry2);
        }

        public IReadOnlyList<GiftRegistry> GetAll() =>
            _registries.Where(r => r.IsPublic).ToList().AsReadOnly();

        public GiftRegistry GetById(int id) =>
            _registries.FirstOrDefault(r => r.Id == id);

        public GiftRegistry GetByShareCode(string shareCode) =>
            _registries.FirstOrDefault(r =>
                r.ShareCode != null &&
                r.ShareCode.Equals(shareCode, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<GiftRegistry> GetByCustomerId(int customerId) =>
            _registries.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();

        public void Add(GiftRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.Id = _nextId++;
            registry.CreatedAt = DateTime.Now;
            if (string.IsNullOrEmpty(registry.ShareCode))
                registry.ShareCode = GenerateShareCode();
            _registries.Add(registry);
        }

        public void Update(GiftRegistry registry)
        {
            var existing = GetById(registry?.Id ?? 0);
            if (existing == null) return;

            existing.Name = registry.Name;
            existing.Description = registry.Description;
            existing.Occasion = registry.Occasion;
            existing.EventDate = registry.EventDate;
            existing.IsPublic = registry.IsPublic;
        }

        public void Remove(int id) =>
            _registries.RemoveAll(r => r.Id == id);

        public void AddItem(int registryId, GiftRegistryItem item)
        {
            var registry = GetById(registryId);
            if (registry == null || item == null) return;

            item.Id = _nextItemId++;
            item.RegistryId = registryId;
            item.AddedAt = DateTime.Now;
            registry.Items.Add(item);
        }

        public void FulfillItem(int registryId, int itemId, string fulfilledBy)
        {
            var registry = GetById(registryId);
            var item = registry?.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;

            item.Status = GiftRegistryItemStatus.Fulfilled;
            item.FulfilledBy = fulfilledBy;
            item.FulfilledAt = DateTime.Now;
        }

        public void RemoveItem(int registryId, int itemId)
        {
            var registry = GetById(registryId);
            registry?.Items.RemoveAll(i => i.Id == itemId);
        }

        private static string GenerateShareCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[10];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var code = new char[10];
            for (int i = 0; i < code.Length; i++)
                code[i] = chars[bytes[i] % chars.Length];
            return new string(code);
        }
    }
}
