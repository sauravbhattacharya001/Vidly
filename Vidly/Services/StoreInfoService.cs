using System;
using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Provides store location and hours information.
    /// </summary>
    public class StoreInfoService
    {
        /// <summary>
        /// Returns all store locations with their hours.
        /// </summary>
        public IReadOnlyList<StoreInfo> GetAllStores()
        {
            return new List<StoreInfo>
            {
                new StoreInfo
                {
                    Id = 1,
                    Name = "Vidly Downtown",
                    Address = "123 Main Street",
                    City = "Springfield",
                    State = "IL",
                    ZipCode = "62701",
                    Phone = "(217) 555-0100",
                    Email = "downtown@vidly.com",
                    Latitude = 39.7817,
                    Longitude = -89.6501,
                    Hours = BuildDefaultHours(
                        weekdayOpen: new TimeSpan(9, 0, 0),
                        weekdayClose: new TimeSpan(21, 0, 0),
                        saturdayOpen: new TimeSpan(10, 0, 0),
                        saturdayClose: new TimeSpan(22, 0, 0),
                        sundayOpen: new TimeSpan(11, 0, 0),
                        sundayClose: new TimeSpan(19, 0, 0)),
                    SpecialDays = GetHolidaySchedule()
                },
                new StoreInfo
                {
                    Id = 2,
                    Name = "Vidly Westside Mall",
                    Address = "456 Commerce Blvd, Suite 200",
                    City = "Springfield",
                    State = "IL",
                    ZipCode = "62704",
                    Phone = "(217) 555-0200",
                    Email = "westside@vidly.com",
                    Latitude = 39.7650,
                    Longitude = -89.6900,
                    Hours = BuildDefaultHours(
                        weekdayOpen: new TimeSpan(10, 0, 0),
                        weekdayClose: new TimeSpan(21, 0, 0),
                        saturdayOpen: new TimeSpan(10, 0, 0),
                        saturdayClose: new TimeSpan(21, 0, 0),
                        sundayOpen: new TimeSpan(12, 0, 0),
                        sundayClose: new TimeSpan(18, 0, 0)),
                    SpecialDays = GetHolidaySchedule()
                }
            };
        }

        /// <summary>
        /// Gets a specific store by ID.
        /// </summary>
        public StoreInfo GetStoreById(int id)
        {
            var stores = GetAllStores();
            foreach (var store in stores)
            {
                if (store.Id == id)
                    return store;
            }
            return null;
        }

        private static List<StoreHours> BuildDefaultHours(
            TimeSpan weekdayOpen, TimeSpan weekdayClose,
            TimeSpan saturdayOpen, TimeSpan saturdayClose,
            TimeSpan sundayOpen, TimeSpan sundayClose)
        {
            var hours = new List<StoreHours>();

            // Monday through Friday
            for (int i = 1; i <= 5; i++)
            {
                hours.Add(new StoreHours
                {
                    DayOfWeek = (DayOfWeek)i,
                    OpenTime = weekdayOpen,
                    CloseTime = weekdayClose
                });
            }

            // Saturday
            hours.Add(new StoreHours
            {
                DayOfWeek = DayOfWeek.Saturday,
                OpenTime = saturdayOpen,
                CloseTime = saturdayClose
            });

            // Sunday
            hours.Add(new StoreHours
            {
                DayOfWeek = DayOfWeek.Sunday,
                OpenTime = sundayOpen,
                CloseTime = sundayClose
            });

            return hours;
        }

        private static List<SpecialHours> GetHolidaySchedule()
        {
            var year = DateTime.Today.Year;
            return new List<SpecialHours>
            {
                new SpecialHours { Date = new DateTime(year, 1, 1), Label = "New Year's Day", IsClosed = true },
                new SpecialHours { Date = new DateTime(year, 7, 4), Label = "Independence Day", IsClosed = true },
                new SpecialHours { Date = new DateTime(year, 11, 27), Label = "Thanksgiving", IsClosed = true },
                new SpecialHours { Date = new DateTime(year, 12, 25), Label = "Christmas Day", IsClosed = true },
                new SpecialHours
                {
                    Date = new DateTime(year, 12, 24),
                    Label = "Christmas Eve",
                    OpenTime = new TimeSpan(9, 0, 0),
                    CloseTime = new TimeSpan(15, 0, 0)
                },
                new SpecialHours
                {
                    Date = new DateTime(year, 12, 31),
                    Label = "New Year's Eve",
                    OpenTime = new TimeSpan(9, 0, 0),
                    CloseTime = new TimeSpan(18, 0, 0)
                }
            };
        }
    }
}
