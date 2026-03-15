using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class QuizController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly MovieQuizService _quizService;

        public QuizController()
            : this(new InMemoryMovieRepository(), new InMemoryCustomerRepository())
        {
        }

        public QuizController(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _quizService = new MovieQuizService(
                _movieRepository.GetMovies());
        }

        // GET: Quiz
        public ActionResult Index()
        {
            var vm = new QuizViewModel
            {
                Leaderboard = _quizService.GetLeaderboard(10),
                DailyChallenge = _quizService.GetDailyChallenge(),
                Customers = _customerRepository.GetCustomers().ToList()
            };
            return View(vm);
        }

        // POST: Quiz/Start
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Start(int customerId, string difficulty, string category,
            int questionCount = 10, int timeLimit = 0)
        {
            try
            {
                var diff = (QuizDifficulty)Enum.Parse(typeof(QuizDifficulty), difficulty);
                var cat = (QuizCategory)Enum.Parse(typeof(QuizCategory), category);

                var session = _quizService.StartQuiz(customerId, diff, cat,
                    questionCount, timeLimit);

                return RedirectToAction("Play", new { sessionId = session.Id, customerId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        // GET: Quiz/Play?sessionId=1&customerId=1
        public ActionResult Play(int sessionId, int customerId)
        {
            var session = _quizService.GetSession(sessionId);
            if (session == null)
            {
                TempData["Error"] = "Quiz session not found.";
                return RedirectToAction("Index");
            }

            if (session.Status != QuizStatus.InProgress)
                return RedirectToAction("Results", new { sessionId, customerId });

            int answeredCount = session.Answers.Count;
            if (answeredCount >= session.Questions.Count)
                return RedirectToAction("Results", new { sessionId, customerId });

            var vm = new QuizViewModel
            {
                ActiveSession = session,
                CurrentQuestion = session.Questions[answeredCount],
                CurrentQuestionIndex = answeredCount + 1,
                TotalQuestions = session.Questions.Count,
                SelectedCustomerId = customerId
            };

            return View(vm);
        }

        // POST: Quiz/Answer
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Answer(int sessionId, int questionId,
            int selectedOption, int customerId)
        {
            try
            {
                _quizService.SubmitAnswer(sessionId, questionId, selectedOption);
                var session = _quizService.GetSession(sessionId);

                if (session.Status != QuizStatus.InProgress)
                    return RedirectToAction("Results", new { sessionId, customerId });

                return RedirectToAction("Play", new { sessionId, customerId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        // GET: Quiz/Results?sessionId=1&customerId=1
        public ActionResult Results(int sessionId, int customerId)
        {
            var session = _quizService.GetSession(sessionId);
            if (session == null)
            {
                TempData["Error"] = "Quiz session not found.";
                return RedirectToAction("Index");
            }

            var vm = new QuizViewModel
            {
                CompletedSession = session,
                CustomerStats = _quizService.GetCustomerStats(customerId),
                Leaderboard = _quizService.GetLeaderboard(10),
                SelectedCustomerId = customerId
            };

            return View(vm);
        }

        // GET: Quiz/Leaderboard
        public ActionResult Leaderboard()
        {
            var vm = new QuizViewModel
            {
                Leaderboard = _quizService.GetLeaderboard(20)
            };
            return View(vm);
        }
    }
}
