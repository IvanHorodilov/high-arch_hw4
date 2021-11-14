using BooksApi.Models;
using BooksApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BooksApi.Controllers
{
    public class CacheItem<T>
    {
        public T Value { get; set; }
        public DateTimeOffset TTL { get; set; }
        public TimeSpan Delta { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class BooksController : ControllerBase
    {
        private readonly BookService _bookService;
        private readonly IDistributedCache _cache;
        private readonly int _userCount;
        private Random _rand = new Random();

        public BooksController(BookService bookService, IDistributedCache cache, IConfiguration configuration)
        {
            _bookService = bookService;
            _cache = cache;
            _userCount = configuration.GetValue<int>("UsersCount");
        }

        [HttpGet]
        public ActionResult<List<Book>> Get() =>
            _bookService.Get();

        [HttpGet("top")]
        public ActionResult<List<Book>> Top([FromQuery] int count) => _bookService.GetTop(count);

        [HttpGet("top-cache")]
        public async Task<ActionResult<List<Book>>> TopWithCacheAsync([FromQuery] int count)
        {
            var userId = _rand.Next(1, _userCount).ToString();
            var cached = JsonConvert.DeserializeObject<CacheItem<List<Book>>>(await _cache.GetStringAsync(userId));
            var beta = 1;
            if (cached is null || DateTimeOffset.Now - cached.Delta * beta * Math.Log(_rand.NextDouble()) >= cached.TTL)
            {
                var sw = Stopwatch.StartNew();
                var result = _bookService.GetTop(count);
                sw.Stop();
                var expiryDate = DateTimeOffset.UtcNow.AddSeconds(20);
                var cacheItem = new CacheItem<List<Book>> { Value = result, TTL = expiryDate, Delta = sw.Elapsed };
                await _cache.SetStringAsync(userId, JsonConvert.SerializeObject(cacheItem), new DistributedCacheEntryOptions { AbsoluteExpiration = expiryDate });
                return result;
            }

            return cached.Value;
        }

        [HttpGet("{id:length(24)}", Name = "GetBook")]
        public ActionResult<Book> Get(string id)
        {
            var book = _bookService.Get(id);

            if (book == null)
            {
                return NotFound();
            }

            return book;
        }

        [HttpPost]
        public ActionResult<Book> Create(Book book)
        {
            _bookService.Create(book);

            return CreatedAtRoute("GetBook", new { id = book.Id.ToString() }, book);
        }

        [HttpPut("{id:length(24)}")]
        public IActionResult Update(string id, Book bookIn)
        {
            var book = _bookService.Get(id);

            if (book == null)
            {
                return NotFound();
            }

            _bookService.Update(id, bookIn);

            return NoContent();
        }

        [HttpDelete("{id:length(24)}")]
        public IActionResult Delete(string id)
        {
            var book = _bookService.Get(id);

            if (book == null)
            {
                return NotFound();
            }

            _bookService.Remove(book.Id);

            return NoContent();
        }
    }
}