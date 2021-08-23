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
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Phrook.Tests
{
    public class EfCoreWishlistServiceTests : IDisposable
    {
        private readonly PhrookDbContext _dbContext;
        private EfCoreWishlistService service;
        private EfCoreBookService booksService;
        private readonly IOptionsMonitor<BooksOptions> booksOptionsMonitor;
        private const string userId0 = "c6d3babc-1947-4806-ba13-c6385a63f726";
        private const string userId1 = "720883f6-2029-4a4a-a7e1-a45dac9b644d";
        private const string userId2 = "7b0b5f93-968e-4172-8d3fc545ffe3fbfe";
        private const string userId3 = "8e9abf8c-f64d-4204-bf30-491f1d4c54d7";
        private const string _isbn = "97888000000";
		
        public EfCoreWishlistServiceTests()
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
            booksOptionsMonitor = Mock.Of<IOptionsMonitor<BooksOptions>>(_ => _.CurrentValue == booksOptions);

            #region mocking EfCoreBooksService
            ILogger<EfCoreBookService> loggerEF = Mock.Of<ILogger<EfCoreBookService>>();

            booksOptionsMonitor = Mock.Of<IOptionsMonitor<BooksOptions>>(_ => _.CurrentValue == booksOptions);

            booksService = new(googleBooksClient: null, _dbContext, booksOptionsMonitor, loggerEF);
            #endregion

            //mock the service for testing
            service = new(googleBooksClient: null, bookService: booksService, _dbContext);
        }

        [Theory]
        [InlineData(userId0, 15)] //userId0
        [InlineData(userId1, 11)] //userId1
        [InlineData(userId2, 0)] //userId2
        [InlineData(null, 0)]
        [InlineData("", 0)]
        public void GetBooksAsync_ShouldWork(string userId, int expectedTotalNumberOfBooks)
        {
            Assert.Equal(expectedTotalNumberOfBooks, service.GetBooksAsync(userId, null).Result.TotalCount);
        }

        [Theory]
        [InlineData(userId0, "BookId22")] //book in wishlist
        [InlineData(userId1, "BookId26")] //book in wishlist
        public void RemoveBookFromWishlist_ShouldWork(string userId, string bookId)
        {
            Assert.True(booksService.IsBookInWishList(userId, bookId).Result);
            service.RemoveBookFromWishlist(userId, bookId);
            Assert.False(booksService.IsBookInWishList(userId, bookId).Result);
        }

        [Theory]
        [InlineData(userId0, "BookId0")] //book not in its wishlist
        [InlineData(userId1, "BookId99")] //book doesn't exist
        [InlineData("not exists", "BookId0")] //user doesn't exist
        [InlineData(null, "BookId0")] //user null
        [InlineData(userId0, null)] //book null
        public async void RemoveBookFromWishlist_ShouldNotWork(string userId, string bookId)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.RemoveBookFromWishlist(userId, bookId));
        }

        [Theory]
        [InlineData(userId0, "BookId16")] //book not in its wishlist, but in books
        [InlineData(userId1, "BookId16")] //book not in its wishlist, but in books
        [InlineData(userId1, "BookId17")] //book not in its wishlist, but in books
        public void AddBookToWishlist_AlreadyInBooks_ShouldWork(string userId, string bookId)
        {
            Assert.False(booksService.IsBookInWishList(userId, bookId).Result);
            service.AddBookToWishlist(userId, bookId);
            Assert.True(booksService.IsBookInWishList(userId, bookId).Result);
        }

        [Theory]
        [InlineData(userId0, "_5RCngEACAAJ")] //book not in its wishlist and not in books
        [InlineData(userId1, "Arw-jgEACAAJ")] //book not in its wishlist and not in books
        public void AddBookToWishlist_NotInBooks_ShouldWork(string userId, string bookId)
        {
            service = getFullService(bookId);
            Assert.False(booksService.IsBookInWishList(userId, bookId).Result);
            service.AddBookToWishlist(userId, bookId);
            Assert.True(booksService.IsBookInWishList(userId, bookId).Result);
        }

        private EfCoreWishlistService getFullService(string bookId)
        {
            #region mocking IGoogleBooksClient
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
            #endregion

            service = new(gbClient, booksService, _dbContext);

            return service;
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
                new ApplicationUser { Id = userId2, UserName = "test3@phrook.it", NormalizedUserName = "TEST3@PHROOK.IT", Email = "test3@phrook.it", NormalizedEmail = "TEST3@PHROOK.IT", EmailConfirmed = true, FullName = "Sofonisba Anguissola", Visibility = true, NormalizedFullName = "sofonisba anguissola" }
            };
            dbContext.Users.AddRange(users);
            dbContext.SaveChanges();
            #endregion

            var libraryBooks = new List<LibraryBook>();
            for (int i = 0; i < 15; i++)
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

                if (i < 6)
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
            for (int i = 20; i < 35; i++)
            {
                Wishlist w = new Wishlist { UserId = userId0, BookId = "BookId" + i, Isbn = _isbn + (i < 10 ? "0" : "") + i, Title = "Libro " + i, NormalizedTitle = "libro " + i, ImagePath = "", Author = "Autore " + i };
                wishlists.Add(w);

                if (i > 23)
                {
                    Wishlist w1 = new Wishlist { UserId = userId1, BookId = "BookId" + i, Isbn = _isbn + (i < 10 ? "0" : "") + i, Title = "Libro " + i, NormalizedTitle = "libro " + i, ImagePath = "", Author = "Autore " + i };
                    wishlists.Add(w1);
                }
            }
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
