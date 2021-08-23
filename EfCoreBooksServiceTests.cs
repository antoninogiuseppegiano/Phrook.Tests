using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Phrook.Models.Entities;
using Phrook.Models.InputModels;
using Phrook.Models.Options;
using Phrook.Models.Services.Application;
using Phrook.Models.Services.HttpClients;
using Phrook.Models.Services.Infrastructure;
using Phrook.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Phrook.Tests
{
    public class EfCoreBooksServiceTests : IDisposable
    {
        private readonly PhrookDbContext _dbContext;
        private readonly IOptionsMonitor<BooksOptions> booksOptionsMonitor;
        private readonly ILogger<EfCoreBookService> loggerEF;
        private const string userId0 = "c6d3babc-1947-4806-ba13-c6385a63f726";
        private const string userId1 = "720883f6-2029-4a4a-a7e1-a45dac9b644d";
        private const string userId2 = "7b0b5f93-968e-4172-8d3fc545ffe3fbfe";
        private const string _isbn = "97888000000";
        private EfCoreBookService service;

        public EfCoreBooksServiceTests()
        {
            #region mocking dbContext
            //mock InMemory database and dbContext
            var serviceCollection = new ServiceCollection();
            var dbServiceProvider = serviceCollection
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
            var options = new DbContextOptionsBuilder<PhrookDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()) //name must be provided
                .UseInternalServiceProvider(dbServiceProvider)
                .Options;
            _dbContext = new PhrookDbContext(options);
            _dbContext.Database.EnsureCreated();
            #endregion

            SeedStatically(_dbContext);

            BooksOptions booksOptions = new()
            {
                PerPage = 10,
                Order = new()
                {
                    By = "Title",
                    Ascending = true,
                    Allow = new string[] { "Title", "Rating", "Tag", "ReadingState" }
                }
            };

            loggerEF = Mock.Of<ILogger<EfCoreBookService>>();

            booksOptionsMonitor = Mock.Of<IOptionsMonitor<BooksOptions>>(_ => _.CurrentValue == booksOptions);

            //mock the service for testing
            service = new(googleBooksClient: null, _dbContext, booksOptionsMonitor, loggerEF);
        }

        [Theory]
        [InlineData(userId0, "", 2, 10, 25)] //userId0, 2nd page
        [InlineData(userId0, "", 3, 5, 25)] //userId0, 3rd page
        [InlineData(userId0, "", 0, 10, 25)] //userId0, page 0 (it will be 1 as default)
        [InlineData(userId1, "", 2, 2, 12)] //userId1
        [InlineData(userId2, "", 2, 0, 0)] //userId2

        [InlineData(userId0, "Not working research title string..." , 1, 0, 0)] //userId0
        [InlineData(userId0, "Libro", 3, 5, 25)] //userId0
        [InlineData(userId0, "Libro 0" , 1, 1, 1)] //userId0
        [InlineData(userId0, "Libro 0", 6, 1, 1)] //userId0, 6th page (it will be 1 as default)
        [InlineData(userId2, "Libro", 1, 0, 0)] //userId2, "libro" -> no results
        public void GetBooksAsync_BooksNumber(string userId, string search, int page, int expectedNumberOfBooks, int expectedTotalNumberOfBooks)
        {
            //mock input model
            string orderBy = "Title";
            bool ascending = true;
            int limit = 10;
            BookListInputModel model = new(search, page, orderBy, ascending, limit, booksOptionsMonitor.CurrentValue.Order);

            //check total # of books
            Assert.Equal(expectedTotalNumberOfBooks, service.GetBooksAsync(userId, model).Result.TotalCount);
            //check books displayed
            Assert.Equal(expectedNumberOfBooks, service.GetBooksAsync(userId, model).Result.Results.Count);
        }

        [Theory]
        [InlineData(userId0, _isbn + "00")] //userId0
        [InlineData(userId0, _isbn + "24")] //userId0
        [InlineData(userId1, _isbn + "05")] //userId1
        public void GetBookByISBNAsync_ShouldWork(string userId, string ISBN)
        {
            Assert.Equal(ISBN, service.GetBookByISBNAsync(userId, ISBN).Result.ISBN);
        }

        [Theory]
        [InlineData(userId0, _isbn + "99")] //userId0, book not exists
        [InlineData(userId0, "")] //userId0, book not exists
        [InlineData(userId0, null)] //userId0, book null
        [InlineData(userId0, "123")] //userId0, book not exists
        [InlineData(userId2, _isbn + "05")] //userId2, book not in its library
        public void GetBookByISBNAsync_ShouldNotWork(string userId, string ISBN)
        {
            Assert.ThrowsAny<Exception>(() => service.GetBookByISBNAsync(userId, ISBN).Result);
        }

        [Theory]
        [InlineData(userId0, "BookId0", 3.6, "0", "0", "2021-05-05", "2021-05-05")] //change rating
        [InlineData(userId0, "BookId0", 0, null, null, "", "")] //change rating =0
        [InlineData(userId0, "BookId0", 5, null, null, "", "")] //change rating =5
        public void EditBookAsync_Rating_ShouldWork(string userId, string bookId, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };
            Assert.Equal(rating, service.EditBookAsync(userId, inputModel).Result.Rating);
        }

        [Theory]
        [InlineData(userId0, "BookId0", 5, "1", null, "", "")] //change tag
        [InlineData(userId0, "BookId0", 5, "3", null, "", "")] //change tag
        public void EditBookAsync_Tag_ShouldWork(string userId, string bookId, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };
            
            //change index into description
            string tagValue;
            switch (tag)
            {
                case "0":
                    tagValue = "Narrativa";
                    break;
                case "1":
                    tagValue = "Saggistica";
                    break;
                case "2":
                    tagValue = "Giallo";
                    break;
                case "3":
                    tagValue = "Fantascienza";
                    break;
                default:
                    tagValue = "ERROR";
                    break;
            }

            Assert.Equal(tagValue, service.EditBookAsync(userId, inputModel).Result.Tag);
        }

        [Theory]
        [InlineData(userId0, "BookId0", 5, null, "2", "", "")] //change reading state
        [InlineData(userId0, "BookId0", 5, null, "0", "", "")] //change reading state
        public void EditBookAsync_ReadingState_ShouldWork(string userId, string bookId, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };

            //change index into description
            string rsValue;
            switch (readingState)
            {
                case "0":
                    rsValue = "Non letto";
                    break;
                case "1":
                    rsValue = "In lettura";
                    break;
                case "2":
                    rsValue = "Interrotto";
                    break;
                case "3":
                    rsValue = "Letto";
                    break;
                default:
                    rsValue = "ERROR";
                    break;
            }

            Assert.Equal(rsValue, service.EditBookAsync(userId, inputModel).Result.ReadingState);
        }

        [Theory]
        [InlineData(userId0, "BookId0", 5, null, "0", "", "")] //change reading state (NotRead) time not changing
        [InlineData(userId0, "BookId0", 5, null, "1", "2021-05-04", "")] //change initial time (Reading)
        [InlineData(userId0, "BookId0", 5, null, "3", "2021-05-02", "2021-05-06")] //change initial time and final time (Read)
        [InlineData(userId0, "BookId0", 5, null, "3", "2100-01-01", "today")] //change final time (Read) initial time = today as default, final time = today
        [InlineData(userId0, "BookId0", 5, null, "3", "2100-01-01", "2100-01-01")] //change intial and final time as default (Read)
        [InlineData(userId0, "BookId0", 5, null, "0", "2021-05-05", "")] //change initial time (NotRead)
        [InlineData(userId0, "BookId0", 5, null, "1", "2021-05-05", "2021-05-06")] //change intial and final time (Reading)
        public void EditBookAsync_Times_ShouldWork(string userId, string bookId, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };
            BookDetailViewModel book = service.EditBookAsync(userId, inputModel).Result;

            DateTime id = DateTime.ParseExact(book.InitialTime, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                fd = DateTime.ParseExact(book.FinalTime, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            
            //dates validation
            Assert.True(DateTime.Compare(id, fd) <= 0 || fd.Equals(DateTime.MinValue.Date));
        }

        [Theory]
        [InlineData(userId0, "BookId0", "New Title", 5, null, null, "", "")] //change title
        public void EditBookAsync_Title_ShouldNotWork(string userId, string bookId, string title, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };
            Assert.NotEqual(title, service.EditBookAsync(userId, inputModel).Result.Title);
        }

        [Theory]
        [InlineData(userId0, "BookId0", -3, null, null, "", "")] //change rating <0
        [InlineData(userId0, "BookId0", 10, null, null, "", "")] //change rating >5
        [InlineData(userId0, "BookId35", 5, null, null, "", "")] //change rating (book not in user's library)
        [InlineData(userId0, "BookId99", 5, null, null, "", "")] //change rating (book not existing in db)
        [InlineData(userId0, "BookId0", 5, "10", null, "", "")] //change tag (not valid)
        [InlineData(userId0, "BookId0", 5, null, "10", "", "")] //change reading state (not valid)
        public void EditBookAsync_ShouldNotWork(string userId, string bookId, double rating, string tag, string readingState, string s_initialTime, string s_finalTime)
        {
            //convert in DateTime
            setTimes(s_initialTime, s_finalTime, out DateTime initialTime, out DateTime finalTime);

            EditBookInputModel inputModel = new() { BookId = bookId, Rating = rating, Tag = tag, ReadingState = readingState, InitialTime = initialTime, FinalTime = finalTime };
            Assert.ThrowsAny<Exception>(() => service.EditBookAsync(userId, inputModel).Result);
        }

        [Theory]
        [InlineData(userId0, "BookId0")]
        [InlineData(userId0, "BookId1")]
        [InlineData(userId1, "BookId6")]
        public void RemoveBookFromLibrary_StillInLibrary_ShouldWork(string userId, string bookId)
        {
            service.RemoveBookFromLibrary(userId, bookId);
            Assert.False(service.IsBookAddedToLibrary(userId, bookId).Result);
            Assert.True(service.IsBookStoredInBooks(bookId).Result);
        }

        [Theory]
        [InlineData(userId0, "BookId24")]
        public void RemoveBookFromLibrary_NotStillInLibrary_ShouldWork(string userId, string bookId)
        {
            service.RemoveBookFromLibrary(userId, bookId);
            Assert.False(service.IsBookAddedToLibrary(userId, bookId).Result);
            Assert.False(service.IsBookStoredInBooks(bookId).Result);
        }

        [Theory]
        [InlineData(userId1, "BookId35")] //book not in user's library
        [InlineData(userId2, "BookId1")] //book not in user's library
        [InlineData(userId0, "BookId99")] //book not existing in db
        public async System.Threading.Tasks.Task RemoveBookFromLibrary_ShouldNotWork(string userId, string bookId)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.RemoveBookFromLibrary(userId, bookId));
        }

        [Theory]
        [InlineData(userId0, "_5RCngEACAAJ")] //book not in wishlist (Il miglio verde)
        [InlineData(userId0, "Arw-jgEACAAJ")] //book in wishlist (Il grande Gatsby)
        public void AddBookToLibrary_NotInBooks_ShouldWork(string userId, string bookId)
        {
            #region mocking IGoogleBooksClient
            //mock IGoogleBooksClient
            ILogger<GoogleBooksClient> loggerGB = Mock.Of<ILogger<GoogleBooksClient>>();
            IOptionsMonitor<GoogleBooksApiOptions> gbApiOptionsMonitor = Mock.Of<IOptionsMonitor<GoogleBooksApiOptions>>(_ =>
                _.CurrentValue == new GoogleBooksApiOptions()
                {
                    Url = "https://www.googleapis.com/books/v1/volumes"
                }
            );
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               // Setup the PROTECTED method to mock
               .Setup<Task<HttpResponseMessage>>(
                  "SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>()
               )
               // prepare the expected response of the mocked http call
               .ReturnsAsync(new HttpResponseMessage()
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(File.ReadAllText(@$"resources/response{bookId}.json")),
               })
               .Verifiable();
            // use real http client with mocked handler here
            var httpClient = new HttpClient(handlerMock.Object);

            GoogleBooksClient gbClient = new(httpClient, loggerGB, gbApiOptionsMonitor);

            service = new(googleBooksClient: gbClient, _dbContext, booksOptionsMonitor, loggerEF);
            #endregion

            service.AddBookToLibrary(userId, bookId);
            Assert.True(service.IsBookStoredInBooks(bookId).Result);
            Assert.True(service.IsBookAddedToLibrary(userId, bookId).Result);
            Assert.False(service.IsBookInWishList(userId, bookId).Result);
        }

        [Theory]
        //userId just for readability reasons
        [InlineData(userId2, "BookId1")]
        public void GetBookNotAddedInLibaryByIdAsync_ShouldWork(string userId, string bookId)
        {
            Assert.Equal(bookId, service.GetBookNotAddedInLibaryByIdAsync(bookId).Result.Id);
        }

        [Theory]
        //userId just for readability reasons
        [InlineData(userId2, "")] //book id empty
        [InlineData(userId2, null)] //book id null
        [InlineData(userId2, "BookId99")] //book doesn't exist
        public async void GetBookNotAddedInLibaryByIdAsync_ShouldNotWork(string userId, string bookId)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetBookNotAddedInLibaryByIdAsync(bookId));
        }

        private void setTimes(string s_initialTime, string s_finalTime, out DateTime initialTime, out DateTime finalTime)
        {
            if (string.IsNullOrWhiteSpace(s_initialTime))
            {
                initialTime = DateTime.MinValue.Date;
            }
            else
            {
                initialTime = DateTime.ParseExact(s_initialTime, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(s_finalTime))
            {
                finalTime = DateTime.MinValue.Date;
            }
            else if (s_finalTime == "today")
            {
                finalTime = DateTime.Now.Date;
            }
            else
            {
                finalTime = DateTime.ParseExact(s_finalTime, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        private void SeedStatically(PhrookDbContext dbContext)
        {
            #region books and users
            var books = new List<Book>();
            for (int i = 0; i < 35; i++)
            {
                books.Add(new Book(bookId: "BookId" + i, isbn: _isbn + (i < 10 ? "0" : "") + i, title: "Libro " + i, description: "Descrizione " + i, author: "Autore s" + i, imagePath: ""));
            }
            dbContext.Books.AddRange(books);
            dbContext.SaveChanges();

            var users = new[]
            {
                new ApplicationUser { Id = userId0, UserName = "test@phrook.it", NormalizedUserName = "TEST@PHROOK.IT", Email = "test@phrook.it", NormalizedEmail = "TEST@PHROOK.IT", EmailConfirmed = true, FullName = "Cosimo de Medici", Visibility = true, NormalizedFullName = "cosimo de medici" },
                new ApplicationUser { Id = userId1, UserName = "test@phrook.it1", NormalizedUserName = "TEST@PHROOK.IT1", Email = "test@phrook.it1", NormalizedEmail = "TEST@PHROOK.IT1", EmailConfirmed = true, FullName = "Maria Callas", Visibility = true, NormalizedFullName = "maria callas" },
                new ApplicationUser { Id = userId2, UserName = "test3@phrook.it", NormalizedUserName = "TEST3@PHROOK.IT", Email = "test3@phrook.it", NormalizedEmail = "TEST3@PHROOK.IT", EmailConfirmed = true, FullName = "Sofonisba Anguissola", Visibility = true, NormalizedFullName = "sofonisba anguissola" },
            };
            dbContext.Users.AddRange(users);
            dbContext.SaveChanges();
            #endregion

            var libraryBooks = new List<LibraryBook>();
            for(int i = 0; i < 25; i++)
            {
                LibraryBook lb = new LibraryBook("BookId" + i, userId0);
                lb.ChangeRating(i % 5);
                lb.ChangeTag((i % 4).ToString());
                lb.ChangeReadingState((i % 4).ToString());
                lb.ChangeInitialTime(DateTime.Now.AddDays(i * (-2)));
                if (i % 4 > 2)
                {
                    lb.ChangeFinalTime(DateTime.Now.AddDays(i * (-1)));
                }
                libraryBooks.Add(lb);
                
                if(i < 12)
                {
                    LibraryBook lb1 = new LibraryBook("BookId" + i, userId1);
                    lb1.ChangeRating(i % 5);
                    lb1.ChangeTag((i % 4).ToString());
                    lb1.ChangeReadingState((i % 4).ToString());
                    lb1.ChangeInitialTime(DateTime.Now.AddDays(i * (-2)));
                    if (i % 4 > 2)
                    {
                        lb1.ChangeFinalTime(DateTime.Now.AddDays(i * (-1)));
                    }
                    libraryBooks.Add(lb1);
                }
            }
            dbContext.LibraryBooks.AddRange(libraryBooks);
            dbContext.SaveChanges();

            var wishlists = new List<Wishlist>();
            for (int i = 25; i < 30; i++)
            {
                Wishlist w = new Wishlist { UserId = userId0, BookId = "BookId"+i, Isbn = _isbn + (i < 10 ? "0" : "") + i, Title = "Libro "+i, NormalizedTitle = "libro "+i, ImagePath = "", Author = "Autore "+i  };
                wishlists.Add(w);

                if (i > 27)
                {
                    Wishlist w1 = new Wishlist { UserId = userId1, BookId = "BookId" + i, Isbn = _isbn + (i < 10 ? "0" : "") + i, Title = "Libro " + i, NormalizedTitle = "libro " + i, ImagePath = "", Author = "Autore " + i };
                    wishlists.Add(w1);
                }
            }
            dbContext.Wishlist.Add(new Wishlist { UserId = userId0, BookId = "Arw-jgEACAAJ", Isbn = "9788844043957", Title = "Il grande Gatsby", NormalizedTitle = "il grande gatsby", ImagePath = "http://books.google.com/books/content?id=Arw-jgEACAAJ&printsec=frontcover&img=1&zoom=1&imgtk=AFLRE70_daJYcpx6ZTiHkXMMV4M2j-XUYjpKuHVmZNmEyIK_6RVFlZ5MwCMnZKABJqa1Hn984hMhuQJWRBQEF1ql1xu9Xac9b-PRUz1bE8F66QptQ2ncOp94osz5avx5Swn03-xwV836&source=gbs_api", Author = "Francis Scott Fitzgerald" });
            dbContext.Wishlist.AddRange(wishlists);
            dbContext.SaveChanges();

        }

        public void Dispose()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();
        }
    }
}
