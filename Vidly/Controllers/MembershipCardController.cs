using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Generates printable digital membership cards for customers.
    /// Shows membership tier, ID, stats, benefits, and a visual barcode-style element.
    /// </summary>
    public class MembershipCardController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public MembershipCardController()
            : this(new InMemoryCustomerRepository(), new InMemoryRentalRepository()) { }

        public MembershipCardController(ICustomerRepository customerRepository, IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Customer selection page — pick a customer to generate their membership card.
        /// </summary>
        public ActionResult Index()
        {
            var customers = _customerRepository.GetAll().OrderBy(c => c.Name).ToList();
            return View(customers);
        }

        /// <summary>
        /// Generates and displays the membership card for a specific customer.
        /// </summary>
        public ActionResult Card(int id)
        {
            var customer = _customerRepository.GetById(id);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            var rentals = _rentalRepository.GetByCustomer(id);
            var totalRentals = rentals.Count;
            var totalSpend = rentals.Sum(r => r.TotalCost);
            var onTimeReturns = rentals.Count(r => r.Status == RentalStatus.Returned && r.ReturnDate.HasValue && r.ReturnDate.Value <= r.DueDate);
            var memberDays = customer.MemberSince.HasValue
                ? (int)(DateTime.Today - customer.MemberSince.Value).TotalDays
                : 0;

            var vm = new MembershipCardViewModel
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                Email = customer.Email,
                Phone = customer.Phone,
                MemberSince = customer.MemberSince,
                MembershipDays = memberDays,
                Tier = customer.MembershipType,
                TotalRentals = totalRentals,
                TotalSpend = totalSpend,
                OnTimeReturns = onTimeReturns,
                CardNumber = GenerateCardNumber(customer.Id, customer.MemberSince),
                ValidThrough = DateTime.Today.AddYears(1).ToString("MM/yy"),
                Benefits = GetTierBenefits(customer.MembershipType)
            };

            return View(vm);
        }

        private static string GenerateCardNumber(int customerId, DateTime? memberSince)
        {
            var year = memberSince?.Year ?? DateTime.Today.Year;
            return $"VDL-{year}-{customerId:D6}";
        }

        private static List<string> GetTierBenefits(MembershipType tier)
        {
            var benefits = new List<string> { "Standard rental access" };
            if (tier >= MembershipType.Silver)
            {
                benefits.Add("5% discount on rentals");
                benefits.Add("Up to 3 concurrent rentals");
            }
            if (tier >= MembershipType.Gold)
            {
                benefits.Add("10% discount on rentals");
                benefits.Add("Free reservations");
                benefits.Add("1 grace day on returns");
            }
            if (tier >= MembershipType.Platinum)
            {
                benefits.Add("15% discount on rentals");
                benefits.Add("Priority new releases");
                benefits.Add("Up to 5 concurrent rentals");
                benefits.Add("3 grace days on returns");
            }
            return benefits;
        }
    }

    /// <summary>
    /// View model for the digital membership card.
    /// </summary>
    public class MembershipCardViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public DateTime? MemberSince { get; set; }
        public int MembershipDays { get; set; }
        public MembershipType Tier { get; set; }
        public int TotalRentals { get; set; }
        public decimal TotalSpend { get; set; }
        public int OnTimeReturns { get; set; }
        public string CardNumber { get; set; }
        public string ValidThrough { get; set; }
        public List<string> Benefits { get; set; } = new List<string>();

        public string TierName => Tier.ToString();

        public string TierColor
        {
            get
            {
                switch (Tier)
                {
                    case MembershipType.Silver: return "#C0C0C0";
                    case MembershipType.Gold: return "#FFD700";
                    case MembershipType.Platinum: return "#E5E4E2";
                    default: return "#4A90D9";
                }
            }
        }

        public string TierGradientStart
        {
            get
            {
                switch (Tier)
                {
                    case MembershipType.Silver: return "#757575";
                    case MembershipType.Gold: return "#B8860B";
                    case MembershipType.Platinum: return "#7B68EE";
                    default: return "#2C3E50";
                }
            }
        }

        public string TierGradientEnd
        {
            get
            {
                switch (Tier)
                {
                    case MembershipType.Silver: return "#C0C0C0";
                    case MembershipType.Gold: return "#FFD700";
                    case MembershipType.Platinum: return "#DDA0DD";
                    default: return "#4A90D9";
                }
            }
        }
    }
}
