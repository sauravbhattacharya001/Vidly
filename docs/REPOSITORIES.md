# Vidly Repositories Reference

Complete reference for the repository layer in `Vidly/Repositories/`.
Repositories encapsulate data access behind interfaces, enabling
in-memory implementations for development/testing and future
database-backed implementations.

> **Architecture note:** All current implementations use thread-safe
> in-memory stores (`Dictionary` + `lock`) with defensive cloning.
> See [ARCHITECTURE.md](../ARCHITECTURE.md) for the full system design.

---

## Contents

- [Design Patterns](#design-patterns)
- [Base Interface](#base-interface)
- [Core Repositories](#core-repositories)
  - [ICustomerRepository](#icustomerrepository)
  - [IMovieRepository](#imovierepository)
  - [IRentalRepository](#irentalrepository)
- [Content & Discovery](#content--discovery)
  - [ICollectionRepository](#icollectionrepository)
  - [IReviewRepository](#ireviewrepository)
  - [IWatchlistRepository](#iwatchlistrepository)
  - [ITagRepository](#itagrepository)
  - [IMovieRequestRepository](#imovierequestrepository)
  - [IDirectorRepository](#idirectorrepository)
  - [IPlaylistRepository](#iplaylistrepository)
  - [ITriviaRepository](#itriviarepository)
- [Commerce & Transactions](#commerce--transactions)
  - [ICouponRepository](#icouponrepository)
  - [IGiftCardRepository](#igiftcardrepository)
  - [IPromotionRepository](#ipromotionrepository)
  - [ISubscriptionRepository](#isubscriptionrepository)
  - [IReservationRepository](#ireservationrepository)
  - [ITradeInRepository](#itraderepository)
  - [IPenaltyWaiverRepository](#ipenaltywaierrepository)
- [Store Operations](#store-operations)
  - [IAnnouncementRepository](#iannouncementrepository)
  - [IScreeningRoomRepository](#iscreeningroomrepository)
  - [ILostAndFoundRepository](#ilostandfoundrepository)
  - [IDamageRepository](#idamagerepository)
  - [IDisputeRepository](#idisputerepository)
  - [IGiftRegistryRepository](#igiftregistryrepository)
  - [IWaitlistRepository](#iwaitlistrepository)
  - [IMovieClubRepository](#imovieclubrepository)
  - [StaffScheduleRepository](#staffschedulerepository)
- [Implementation Inventory](#implementation-inventory)
- [Testing](#testing)

---

## Design Patterns

### Interface Segregation

Each domain entity gets its own interface extending `IRepository<T>` with
domain-specific query methods. This keeps interfaces cohesive — controllers
and services depend only on the queries they need.

### Thread Safety

All `InMemory*` implementations use `lock` blocks around shared static
`Dictionary<int, T>` stores. Reads return defensive clones to prevent
callers from mutating internal state outside the lock.

### Static Reset

Each implementation exposes a `static Reset()` method that restores the
repository to its seed data. Tests call this in `[TestInitialize]` for
isolation between test methods.

### Atomic Operations

Business-critical operations are atomic within the lock scope:
- `IRentalRepository.Checkout()` — availability check + rental creation
- `ICouponRepository.TryRedeem()` — validity check + usage increment
- `IRentalRepository.ExtendRental()` — extension check + date update

---

## Base Interface

### IRepository\<T\>

Generic CRUD contract for all entity repositories.

```csharp
public interface IRepository<T> where T : class
{
    T GetById(int id);
    IReadOnlyList<T> GetAll();
    void Add(T entity);
    void Update(T entity);
    void Remove(int id);
}
```

| Method | Behavior |
|--------|----------|
| `GetById` | Returns defensive clone, or `null` if not found |
| `GetAll` | Returns `ReadOnlyList` of clones |
| `Add` | Assigns auto-incremented `Id`, stores entity |
| `Update` | Throws `KeyNotFoundException` if missing |
| `Remove` | Throws `KeyNotFoundException` if missing |

---

## Core Repositories

### ICustomerRepository

**Extends:** `IRepository<Customer>`
**Implementation:** `InMemoryCustomerRepository`

Manages customer accounts with search, filtering, and statistics.

| Method | Description |
|--------|-------------|
| `Search(query, membershipType?)` | Case-insensitive substring match on name/email, optional membership filter |
| `GetByMemberSince(year, month)` | Customers who joined in a specific month |
| `GetStats()` | Single-pass membership breakdown (`CustomerStats`) |

**Seed data:** 5 customers (John Smith, Jane Doe, Bob Wilson, Alice Johnson, Charlie Brown) across Basic/Silver/Gold/Platinum tiers.

---

### IMovieRepository

**Extends:** `IRepository<Movie>`
**Implementation:** `InMemoryMovieRepository`

Movie catalog with release date queries, random selection, and search.

| Method | Description |
|--------|-------------|
| `Search(query, genre?, minRating?)` | Name search with optional genre and rating filters |
| `GetByReleaseDate(year, month)` | Movies released in a specific month |
| `GetRandom()` | Random movie selection, or `null` if empty |

---

### IRentalRepository

**Extends:** `IRepository<Rental>`
**Implementation:** `InMemoryRentalRepository`

The most feature-rich repository — handles the complete rental lifecycle.

| Method | Description |
|--------|-------------|
| `GetByCustomer(customerId)` | All rentals for a customer (any status) |
| `GetActiveByCustomer(customerId)` | Non-returned rentals only |
| `GetByMovie(movieId)` | All rentals for a movie |
| `GetOverdue()` | Rentals past due date, not returned |
| `Search(query, status?)` | Search by customer/movie name with status filter |
| `ReturnRental(rentalId)` | Mark returned, calculate late fees |
| `IsMovieRentedOut(movieId)` | Check if currently rented |
| `Checkout(rental)` | **Atomic** — availability check + create |
| `Checkout(rental, maxConcurrentRentals)` | **Atomic** — availability + concurrent limit check + create |
| `ExtendRental(rentalId, days)` | Extend due date (once, 1–7 days, adds fee) |
| `IsExtended(rentalId)` | Check extension status |
| `GetStats()` | Dashboard statistics (`RentalStats`) |

**`RentalStats` fields:** `TotalRentals`, `ActiveRentals`, `OverdueRentals`, `ReturnedRentals`, `TotalRevenue`, `RealizedRevenue`, `ProjectedRevenue`, `TotalLateFees`

---

## Content & Discovery

### ICollectionRepository

**Extends:** `IRepository<MovieCollection>`
**Implementation:** `InMemoryCollectionRepository`

Curated themed movie lists.

| Method | Description |
|--------|-------------|
| `GetPublished()` | Visible collections only |
| `Search(query)` | Name substring search |
| `GetByName(name)` | Exact name lookup (case-insensitive) |
| `AddMovie(collectionId, movieId)` | Add movie to collection (returns `false` if duplicate) |
| `RemoveMovie(collectionId, movieId)` | Remove movie from collection |

---

### IReviewRepository

**Extends:** `IRepository<Review>`
**Implementation:** `InMemoryReviewRepository`

Customer reviews with one-review-per-customer-per-movie enforcement.

| Method | Description |
|--------|-------------|
| `GetByMovie(movieId)` | Reviews for a movie, newest first |
| `GetByCustomer(customerId)` | Reviews by a customer, newest first |
| `GetByCustomerAndMovie(customerId, movieId)` | Single review lookup |
| `HasReviewed(customerId, movieId)` | Duplicate check |
| `GetMovieStats(movieId)` | Aggregate `ReviewStats` |
| `GetTopRatedMovies(count, minReviews)` | Top-rated movies by average score |

---

### IWatchlistRepository

**Standalone interface** (does not extend `IRepository<T>`)
**Implementation:** `InMemoryWatchlistRepository`

Customer movie wishlists with priority ordering.

| Method | Description |
|--------|-------------|
| `GetByCustomer(customerId)` | Items ordered by priority (desc), then date (desc) |
| `IsOnWatchlist(customerId, movieId)` | Duplicate check |
| `GetById(id)` / `GetAll()` / `Add()` | Standard CRUD |

---

### ITagRepository

**Standalone interface**
**Implementation:** `InMemoryTagRepository`

Movie tagging system with tag CRUD and movie-tag assignments.

| Method | Description |
|--------|-------------|
| **Tags:** `GetTagById`, `GetTagByName`, `GetAllTags`, `AddTag`, `UpdateTag`, `DeleteTag` | Tag lifecycle |
| **Assignments:** `AddAssignment`, `RemoveAssignment`, `HasAssignment` | Tag-movie links |
| `GetAssignmentsByMovie(movieId)` / `GetAssignmentsByTag(tagId)` | Query assignments |
| `RemoveAllAssignmentsForTag(tagId)` / `RemoveAllAssignmentsForMovie(movieId)` | Bulk cleanup |

---

### IMovieRequestRepository

**Extends:** `IRepository<MovieRequest>`
**Implementation:** `InMemoryMovieRequestRepository`

Customer requests for new titles with voting support.

| Method | Description |
|--------|-------------|
| `GetByCustomer(customerId)` | Requests by customer, newest first |
| `GetByStatus(status)` | Filter by `MovieRequestStatus` |
| `Search(query, status?, genre?)` | Title/reason text search with filters |
| `GetByTitle(title)` | Exact title lookup (case-insensitive) |
| `Vote(requestId, customerId)` | Record vote (returns `false` if duplicate) |

---

### IDirectorRepository

**Standalone interface**
**Implementation:** `InMemoryDirectorRepository`

Director profiles with filmography links.

| Method | Description |
|--------|-------------|
| `GetAll()` / `GetById(id)` | Director CRUD |
| `GetMovieLinks(directorId)` | Director-movie associations |
| `Search(query)` | Name search |

---

### IPlaylistRepository

**Standalone interface**
**Implementation:** `InMemoryPlaylistRepository`

Customer-curated movie playlists with ordering.

| Method | Description |
|--------|-------------|
| `GetByCustomer(customerId)` | Customer's playlists |
| `GetPublicPlaylists(limit)` | Discoverable public playlists |
| `AddEntry` / `RemoveEntry` / `MoveEntry` | Playlist entry management |

---

### ITriviaRepository

**Standalone interface**
**Implementation:** `InMemoryTriviaRepository`

Movie trivia facts with likes and verification.

| Method | Description |
|--------|-------------|
| `GetByMovieId(movieId)` / `GetByCategory(category)` | Filtered queries |
| `Like(id)` / `Verify(id)` | Social signals |
| `GetRandom()` | Random fact |

---

## Commerce & Transactions

### ICouponRepository

**Standalone interface**
**Implementation:** `InMemoryCouponRepository`

Coupon management with atomic redemption.

| Method | Description |
|--------|-------------|
| `GetByCode(code)` | Lookup by coupon code |
| `TryRedeem(code)` | **Atomic** — validate + increment `TimesUsed` |

---

### IGiftCardRepository

**Standalone interface**
**Implementation:** `InMemoryGiftCardRepository`

Gift cards with transaction history.

| Method | Description |
|--------|-------------|
| `GetByCode(code)` | Lookup by card code |
| `AddTransaction(giftCardId, transaction)` | Record charge/reload |

---

### IPromotionRepository

**Standalone interface**
**Implementation:** `InMemoryPromotionRepository`

Standard CRUD for promotional campaigns: `GetAll`, `GetById`, `Add`, `Update`, `Remove`.

---

### ISubscriptionRepository

**Standalone interface**
**Implementation:** `InMemorySubscriptionRepository`

Subscription plans with billing events.

| Method | Description |
|--------|-------------|
| `GetByCustomerId(customerId)` | Active subscription lookup |
| `GetByStatus(status)` | Filter by `SubscriptionStatus` |
| `AddBillingEvent(subscriptionId, evt)` | Record billing history |

---

### IReservationRepository

**Extends:** `IRepository<Reservation>`
**Implementation:** `InMemoryReservationRepository`

Movie holds with queue management.

| Method | Description |
|--------|-------------|
| `GetByCustomer` / `GetByMovie` | Filtered queries |
| `GetActiveByMovie(movieId)` | Waiting or Ready reservations |
| `GetNextInQueue(movieId)` | Next waiting reservation |
| `HasActiveReservation(customerId, movieId)` | Duplicate check |
| `GetExpired()` | Reservations past pickup window |

---

### ITradeInRepository

**Standalone interface**
**Implementation:** `InMemoryTradeInRepository`

Physical media trade-in processing.

| Method | Description |
|--------|-------------|
| `GetByCustomer(customerId)` | Customer trade-ins |
| `GetPending()` | Awaiting review |
| `GetStats()` | `TradeInStats` with format breakdown |

---

### IPenaltyWaiverRepository

**Standalone interface**
**Implementation:** `InMemoryPenaltyWaiverRepository`

Late fee waiver management.

| Method | Description |
|--------|-------------|
| `GetByRental(rentalId)` | Waivers for a rental |
| `GetTotalWaivedForRental(rentalId)` | Sum of waived amounts |
| `GetStats()` | `WaiverStats` (total/full/partial counts + amount) |

---

## Store Operations

### IAnnouncementRepository

**Extends:** `IRepository<Announcement>`
**Implementation:** `InMemoryAnnouncementRepository`

Store announcements with publishing lifecycle.

| Method | Description |
|--------|-------------|
| `GetActive()` | Published, non-expired announcements |
| `GetByCategory(category)` | Filter by `AnnouncementCategory` |
| `GetPinned()` | Pinned announcements |
| `Publish(id)` / `Archive(id)` | Lifecycle transitions |
| `TogglePin(id, staffId)` | Pin/unpin |
| `Search(query)` | Title/body search |

---

### IScreeningRoomRepository

**Standalone interface**
**Implementation:** `InMemoryScreeningRoomRepository`

In-store screening room bookings.

| Method | Description |
|--------|-------------|
| `GetAllRooms()` / `GetRoomById(id)` | Room inventory |
| `GetBookingsByDate` / `GetBookingsByRoom` / `GetBookingsByCustomer` | Booking queries |
| `AddBooking` / `CancelBooking` | Booking lifecycle |
| `IsSlotAvailable(roomId, date, startHour, durationHours)` | Availability check |

---

### ILostAndFoundRepository

**Standalone interface**
**Implementation:** `InMemoryLostAndFoundRepository`

Lost item tracking with claims process.

| Method | Description |
|--------|-------------|
| `GetByStatus` / `GetByCategory` | Filtered queries |
| `AddClaim` / `UpdateClaim` / `GetClaimsForItem` | Claims management |
| `GetOverdueItems()` | Items past retention period |
| `GetReport()` | Aggregate `LostAndFoundReport` |
| `Search(query)` | Item description search |

---

### IDamageRepository

**Standalone interface**
**Implementation:** `InMemoryDamageRepository`

Physical media damage reporting.

| Method | Description |
|--------|-------------|
| `GetByCustomer` / `GetByMovie` | Attribution queries |
| `GetByStatus` / `GetBySeverity` | Filtered views |
| `GetSummary()` | Aggregate `DamageSummary` |

---

### IDisputeRepository

**Standalone interface**
**Implementation:** `InMemoryDisputeRepository`

Customer dispute tracking: `GetByCustomer`, `GetByRental`, `GetByStatus`, plus standard CRUD.

---

### IGiftRegistryRepository

**Standalone interface**
**Implementation:** `InMemoryGiftRegistryRepository`

Shareable movie gift registries.

| Method | Description |
|--------|-------------|
| `GetByShareCode(shareCode)` | Public link lookup |
| `GetByCustomerId(customerId)` | Customer's registries |
| `AddItem` / `RemoveItem` | Registry item management |
| `FulfillItem(registryId, itemId, fulfilledBy)` | Mark gifted |

---

### IWaitlistRepository

**Standalone interface**
**Implementation:** `InMemoryWaitlistRepository`

Waitlist management for out-of-stock titles.

| Method | Description |
|--------|-------------|
| `GetActiveByMovie(movieId)` | Active waitlist for a title |
| `FindExisting(customerId, movieId)` | Duplicate check |
| `GetStats()` | `WaitlistStats` |

---

### IMovieClubRepository

**Standalone interface**
**Implementation:** `InMemoryMovieClubRepository`

Community movie clubs with membership, watchlists, and polls.

| Method | Description |
|--------|-------------|
| `GetByStatus(status)` | Filter by `ClubStatus` |
| `GetMembers` / `AddMember` / `RemoveMember` | Membership management |
| `GetWatchlist` / `AddToWatchlist` / `MarkWatched` | Club watchlist |
| `GetPolls` / `AddPoll` / `Vote` / `ClosePoll` | Poll system |
| `GetStats(clubId)` | Club-level `ClubStats` |

---

### StaffScheduleRepository

**Concrete class** (no interface)
**Implementation:** `StaffScheduleRepository`

Staff shift scheduling — the only repository without an interface abstraction. Uses the same in-memory pattern but is directly instantiated by `StaffSchedulingService`.

---

## Implementation Inventory

| Interface | Implementation | Extends `IRepository<T>` |
|-----------|---------------|:------------------------:|
| `ICustomerRepository` | `InMemoryCustomerRepository` | ✅ |
| `IMovieRepository` | `InMemoryMovieRepository` | ✅ |
| `IRentalRepository` | `InMemoryRentalRepository` | ✅ |
| `ICollectionRepository` | `InMemoryCollectionRepository` | ✅ |
| `IReviewRepository` | `InMemoryReviewRepository` | ✅ |
| `IReservationRepository` | `InMemoryReservationRepository` | ✅ |
| `IAnnouncementRepository` | `InMemoryAnnouncementRepository` | ✅ |
| `IMovieRequestRepository` | `InMemoryMovieRequestRepository` | ✅ |
| `ICouponRepository` | `InMemoryCouponRepository` | ❌ |
| `IGiftCardRepository` | `InMemoryGiftCardRepository` | ❌ |
| `IWatchlistRepository` | `InMemoryWatchlistRepository` | ❌ |
| `ISubscriptionRepository` | `InMemorySubscriptionRepository` | ❌ |
| `ITagRepository` | `InMemoryTagRepository` | ❌ |
| `IDisputeRepository` | `InMemoryDisputeRepository` | ❌ |
| `IDamageRepository` | `InMemoryDamageRepository` | ❌ |
| `IPlaylistRepository` | `InMemoryPlaylistRepository` | ❌ |
| `IPromotionRepository` | `InMemoryPromotionRepository` | ❌ |
| `IScreeningRoomRepository` | `InMemoryScreeningRoomRepository` | ❌ |
| `IDirectorRepository` | `InMemoryDirectorRepository` | ❌ |
| `IGiftRegistryRepository` | `InMemoryGiftRegistryRepository` | ❌ |
| `ILostAndFoundRepository` | `InMemoryLostAndFoundRepository` | ❌ |
| `IMovieClubRepository` | `InMemoryMovieClubRepository` | ❌ |
| `IPenaltyWaiverRepository` | `InMemoryPenaltyWaiverRepository` | ❌ |
| `IPlaylistRepository` | `InMemoryPlaylistRepository` | ❌ |
| `ITradeInRepository` | `InMemoryTradeInRepository` | ❌ |
| `ITriviaRepository` | `InMemoryTriviaRepository` | ❌ |
| `IWaitlistRepository` | `InMemoryWaitlistRepository` | ❌ |
| — | `InMemoryMovieQuoteRepository` | ❌ |
| — | `InMemorySoundtrackRepository` | ❌ |
| — | `StaffScheduleRepository` | ❌ |

---

## Testing

Repository implementations are tested in `Vidly.Tests/`:

- `InMemoryCustomerRepositoryTests`
- `InMemoryMovieRepositoryTests`
- `InMemoryRentalRepositoryTests`

Each test class calls `Reset()` in `[TestInitialize]` for isolation.
Coverage focuses on the three core repositories; domain-specific
repositories are indirectly tested through their consuming services.

```bash
# Run repository tests
dotnet test --filter "FullyQualifiedName~RepositoryTests"
```

For more details on the testing approach, see [TESTING.md](TESTING.md).
