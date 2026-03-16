using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// ViewModel for the Survey dashboard (Index).
    /// </summary>
    public class SurveyViewModel
    {
        public SurveyReport Report { get; set; }
        public List<RentalSurvey> RecentSurveys { get; set; } = new List<RentalSurvey>();
        public List<AtRiskCustomer> AtRiskCustomers { get; set; } = new List<AtRiskCustomer>();
        public List<ImprovementOpportunity> Opportunities { get; set; } = new List<ImprovementOpportunity>();
        public List<SurveyInvitation> PendingInvitations { get; set; } = new List<SurveyInvitation>();
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public List<Rental> CompletedRentals { get; set; } = new List<Rental>();
    }

    /// <summary>
    /// ViewModel for individual survey detail view.
    /// </summary>
    public class SurveyDetailViewModel
    {
        public RentalSurvey Survey { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
    }
}
