using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Generates consolidated rental statements for customers over a date range.
    /// Staff can produce printable invoices showing all rentals, costs, and summary statistics.
    /// </summary>
    public class StatementController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public StatementController()
            : this(new InMemoryCustomerRepository(), new InMemoryRentalRepository()) { }

        public StatementController(ICustomerRepository customerRepository, IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Shows the statement generator form and, if parameters are provided, the generated statement.
        /// </summary>
        public ActionResult Index(int? customerId, DateTime? from, DateTime? to)
        {
            var allCustomers = _customerRepository.GetAll();
            var vm = new StatementViewModel
            {
                AllCustomers = allCustomers,
                CustomerId = customerId,
                PeriodStart = from,
                PeriodEnd = to
            };

            if (!customerId.HasValue)
            {
                vm.StatusMessage = "Select a customer and date range to generate a statement.";
                return View(vm);
            }

            var customer = _customerRepository.GetById(customerId.Value);
            if (customer == null)
            {
                vm.StatusMessage = "Customer not found.";
                return View(vm);
            }

            var periodStart = from ?? DateTime.Today.AddMonths(-1);
            var periodEnd = to ?? DateTime.Today;

            if (periodStart > periodEnd)
            {
                vm.StatusMessage = "Start date must be before end date.";
                vm.PeriodStart = periodStart;
                vm.PeriodEnd = periodEnd;
                return View(vm);
            }

            var statement = BuildStatement(customer, periodStart, periodEnd);
            vm.Statement = statement;
            vm.PeriodStart = periodStart;
            vm.PeriodEnd = periodEnd;

            return View(vm);
        }

        /// <summary>
        /// Renders a print-optimized version of the statement.
        /// </summary>
        public ActionResult Print(int customerId, DateTime from, DateTime to)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            var statement = BuildStatement(customer, from, to);
            return View(statement);
        }

        internal CustomerStatement BuildStatement(Customer customer, DateTime periodStart, DateTime periodEnd)
        {
            var allRentals = _rentalRepository.GetByCustomer(customer.Id);

            // Filter to rentals that overlap with the statement period
            var periodRentals = allRentals
                .Where(r => r.RentalDate <= periodEnd && r.RentalDate >= periodStart)
                .OrderBy(r => r.RentalDate)
                .ToList();

            var lineItems = new List<StatementLineItem>();
            decimal subtotal = 0, totalLateFees = 0;
            double totalDays = 0;
            int active = 0, returned = 0, overdue = 0;

            foreach (var r in periodRentals)
            {
                var endDate = r.ReturnDate ?? DateTime.Today;
                var days = Math.Max(1, (int)Math.Ceiling((endDate - r.RentalDate).TotalDays));
                var rentalCost = days * r.DailyRate;
                var lineTotal = rentalCost + r.LateFee;
                var wasLate = r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate;

                lineItems.Add(new StatementLineItem
                {
                    RentalId = r.Id,
                    MovieName = r.MovieName ?? "Unknown",
                    RentalDate = r.RentalDate,
                    DueDate = r.DueDate,
                    ReturnDate = r.ReturnDate,
                    DaysRented = days,
                    DailyRate = r.DailyRate,
                    RentalCost = rentalCost,
                    LateFee = r.LateFee,
                    LineTotal = lineTotal,
                    Status = r.Status,
                    WasLate = wasLate
                });

                subtotal += rentalCost;
                totalLateFees += r.LateFee;
                totalDays += days;

                switch (r.Status)
                {
                    case RentalStatus.Active: active++; break;
                    case RentalStatus.Returned: returned++; break;
                    case RentalStatus.Overdue: overdue++; break;
                }
            }

            return new CustomerStatement
            {
                Customer = customer,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                GeneratedAt = DateTime.Now,
                StatementNumber = $"STM-{customer.Id:D4}-{periodStart:yyyyMM}",
                LineItems = lineItems,
                Subtotal = subtotal,
                TotalLateFees = totalLateFees,
                GrandTotal = subtotal + totalLateFees,
                TotalRentals = periodRentals.Count,
                ActiveRentals = active,
                ReturnedRentals = returned,
                OverdueRentals = overdue,
                AverageDurationDays = periodRentals.Count > 0 ? Math.Round(totalDays / periodRentals.Count, 1) : 0
            };
        }
    }
}
