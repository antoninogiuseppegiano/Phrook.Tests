using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Phrook.Models.Entities;
using Phrook.Models.InputModels;
using Phrook.Models.Options;
using Phrook.Models.Services.Application;
using Phrook.Models.Services.Infrastructure;
using System;
using System.Collections.Generic;
using Xunit;

namespace Phrook.Tests
{
    public class EfCoreUsersServicesTests : IDisposable
    {
        private readonly PhrookDbContext _dbContext;
        private EfCoreUserService service;
        private readonly IOptionsMonitor<BooksOptions> booksOptionsMonitor;
        private const string userId0 = "c6d3babc-1947-4806-ba13-c6385a63f726";
        private const string userId1 = "720883f6-2029-4a4a-a7e1-a45dac9b644d";
        private const string userId2 = "7b0b5f93-968e-4172-8d3fc545ffe3fbfe";
        private const string userId3 = "8e9abf8c-f64d-4204-bf30-491f1d4c54d7";
        private const string _isbn = "97888000000";

        public EfCoreUsersServicesTests()
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

            //mock the service for testing
            service = new(_dbContext);
        }

        [Theory]
        [InlineData(userId0, "", 2, 10, 25)] //userId0, 2nd page
        [InlineData(userId0, "", 3, 5, 25)] //userId0, 3rd page
        [InlineData(userId0, "", 0, 10, 25)] //userId0, page 0 (it will be 1 as default)
        [InlineData(userId1, "", 2, 2, 12)] //userId1
        [InlineData(userId2, "", 2, 0, 0)] //userId2

        [InlineData(userId0, "Not working research title string...", 1, 0, 0)] //userId0
        [InlineData(userId0, "Libro", 3, 5, 25)] //userId0
        [InlineData(userId0, "Libro 0", 1, 1, 1)] //userId0
        [InlineData(userId0, "Libro 0", 6, 1, 1)] //userId0 6th page (it will be
        [InlineData(userId2, "Libro", 1, 0, 0)] //userId2, "libro" -> no results
        public void GetUserBooks_ShouldWork(string userId, string search, int page, int expectedNumberOfBooks, int expectedTotalNumberOfBooks)
        {
            string orderBy = "Title";
            bool ascending = true;
            int limit = 10;
            BookListInputModel model = new(search, page, orderBy, ascending, limit, booksOptionsMonitor.CurrentValue.Order);

            //check total # of books
            Assert.Equal(expectedTotalNumberOfBooks, service.GetUserBooks(userId, model).Result.TotalCount);
            //check books displayed
            Assert.Equal(expectedNumberOfBooks, service.GetUserBooks(userId, model).Result.Results.Count);
        }

        [Theory]
        [InlineData(userId0, "Cosimo de Medici")]
        [InlineData(userId1, "Maria Callas")]
        [InlineData(userId2, "Sofonisba Anguissola")]
        [InlineData(userId3, "Ennio Morricone")] //not visible
        public void GetUserFullName_ShouldWork(string userId, string fullname)
        {
            Assert.Equal(fullname, service.GetUserFullName(userId).Result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Not exists")]
        public async System.Threading.Tasks.Task GetUserFullName_ShouldNotWork(string userId)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetUserFullName(userId));
        }


        [Theory]
        [InlineData(userId0, "maria", 1)]
        [InlineData(userId0, "A", 2)]
        [InlineData(userId1, "o", 2)]
        [InlineData(userId0, "Cosimo", 0)]
        [InlineData(userId0, "not exists", 0)]
        public void GetUsers_ShouldWork(string userId, string search, int expectedResults)
        {
            Assert.Equal(expectedResults, service.GetUsers(userId, search).Result.TotalCount);
        }

        [Theory]
        [InlineData(userId0, null)]
        public async System.Threading.Tasks.Task GetUsers_ShouldNotWork(string userId, string search)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetUsers(userId, search));
        }

        [Theory]
        [InlineData(userId0, true)]
        [InlineData(userId3, false)]
        public void IsVisible_ShouldWork(string userId, bool visibleExpected)
        {
            Assert.Equal(visibleExpected, service.IsVisible(userId).Result);
        }

        [Theory]
        [InlineData("not exists")]
        [InlineData(null)]
        public async void IsVisible_ShouldNotWork(string userId)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.IsVisible(userId));
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
                new ApplicationUser { Id = userId3, UserName = "test4@phrook.it", NormalizedUserName = "TEST4@PHROOK.IT", Email = "test4@phrook.it", NormalizedEmail = "TEST4@PHROOK.IT", EmailConfirmed = true, FullName = "Ennio Morricone", Visibility = false, NormalizedFullName = "ennio morricone" }
            };
            dbContext.Users.AddRange(users);
            dbContext.SaveChanges();
            #endregion

            var libraryBooks = new List<LibraryBook>();
            for (int i = 0; i < 25; i++)
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

                if (i < 12)
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
                Wishlist w = new Wishlist { UserId = userId0, BookId = "BookId" + i, Isbn = _isbn + (i < 10 ? "0" : "") + i, Title = "Libro " + i, NormalizedTitle = "libro " + i, ImagePath = "", Author = "Autore " + i };
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
