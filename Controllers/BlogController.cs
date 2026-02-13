using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Models.Dto;

namespace TheWanderLustWebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BlogController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        public BlogController(AppDbContext appDbContext, IWebHostEnvironment env, CloudinaryService cloudinaryService)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _dbContext = appDbContext;
            _cloudinaryService = cloudinaryService;
        }

        [HttpPost("postblog")]
        public async Task<IActionResult> CreateBlog([FromForm] BlogReqDto blogreq)
        {
            var blog = new Blog
            {
                Heading = blogreq.Heading,
                Tagline = blogreq.Tagline,
                Content = blogreq.Content,
                Location = blogreq.Location,
                UserEmail = blogreq.UserEmail,
                ImageUrls = new List<string>()
            };

            _dbContext.Blogs.Add(blog);
            await _dbContext.SaveChangesAsync();

            var imageUrls = new List<string>();
            var imagesMetadata = new List<ImageMetadata>();

            if (blogreq.Images != null)
            {
                for (int i = 0; i < blogreq.Images.Count; i++)
                {
                    var file = blogreq.Images[i];
                    var imageUrl = await _cloudinaryService.UploadImageAsync(file, "travel_posts");
                    imageUrls.Add(imageUrl);
                    Console.WriteLine("imageurl", imageUrl);
                    Console.WriteLine("width", blogreq.ImageWidths[i]);
                    Console.WriteLine("height", blogreq.ImageHeights[i]);
                    Console.WriteLine("blogid", blog.Id);
                    imagesMetadata.Add(new ImageMetadata
                    {
                        Url = imageUrl,
                        Width = blogreq.ImageWidths[i],
                        Height = blogreq.ImageHeights[i],
                        BlogId = blog.Id
                    });
                }

                await _dbContext.ImageMetadata.AddRangeAsync(imagesMetadata);
                await _dbContext.SaveChangesAsync();
            }

            blog.ImageUrls = imageUrls;
            _dbContext.Blogs.Update(blog);
            await _dbContext.SaveChangesAsync();

            return Ok(new BlogResDto
            {
                Heading = blog.Heading,
                Tagline = blog.Tagline,
                Content = blog.Content,
                Location = blog.Location,
                UserEmail = blog.UserEmail,
                Images = imageUrls,
                ImageMetadata = imagesMetadata
            });
        }

        [HttpGet("getFeaturedBlogs")]
        public async Task<IActionResult> GetFeaturedBlogs()
        {
            var query = _dbContext.Blogs.Select(blog => new
            {
                Id = blog.Id,
                Email = blog.UserEmail,
                Heading = blog.Heading,
                Tagline = blog.Tagline,
                Content = blog.Content,
                ImageUrls = blog.ImageUrls,
                Location = blog.Location,
                LikeCount = _dbContext.BlogLikes.Count(bl => bl.BlogId == blog.Id),
                Likes = _dbContext.BlogLikes.Where(bl => bl.BlogId == blog.Id).ToList(),
                CreatedAt = blog.CreatedAt
            })
            .OrderByDescending(blog => blog.LikeCount)
            .Take(5);

            var blogs = await query.ToListAsync();
            return Ok(blogs);
        }

        [HttpGet("getallblogs")]
        public async Task<ActionResult<IEnumerable<Blog>>> GetAllBlogs([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest("Page and PageSize must be greater than 0");

            var query = GetBlogQuery();

            int totalBlogs = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalBlogs / pageSize);

            var blogs = await query
                .OrderByDescending(bl => bl.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                blogDetails = blogs,
                currentPage = page,
                pageSize,
                totalPages,
                totalBlogs
            });
        }

        [HttpGet("getBlogById")]
        public async Task<ActionResult<object>> GetBlogById([FromQuery] int blogId)
        {
            var blog = await GetBlogQuery().FirstOrDefaultAsync(b => b.Id == blogId);

            if (blog == null)
                return NotFound("Blog not found");

            return Ok(blog);
        }

        [HttpPost("toggleLike")]
        public async Task<IActionResult> ToggleLike([FromBody] LikeDto likeDto)
        {
            var blog = await _dbContext.Blogs.FindAsync(likeDto.BlogId);
            if (blog is null)
                return NotFound("Blog Not Found");

            var existingLike = await _dbContext.BlogLikes.FirstOrDefaultAsync(bl => bl.BlogId == likeDto.BlogId && bl.UserEmail == likeDto.UserEmail);

            if (existingLike != null)
                _dbContext.BlogLikes.Remove(existingLike);
            else
            {
                var blogLike = new BlogLikes
                {
                    BlogId = likeDto.BlogId,
                    UserEmail = likeDto.UserEmail,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.BlogLikes.Add(blogLike);
            }

            await _dbContext.SaveChangesAsync();

            int likeCount = await _dbContext.BlogLikes
            .Where(bl => bl.BlogId == likeDto.BlogId)
            .CountAsync();

            return Ok(new LikeResponseDto { LikeCount = likeCount });
        }

        [HttpPost("blogComment")]
        public async Task<IActionResult> CommentBlog([FromBody] CommentReqDto commentDto)
        {
            if (commentDto == null)
            {
                return BadRequest("Comment is null");
            }

            var comment = new BlogComments
            {
                Author = commentDto.Author,
                CreatedAt = DateTime.UtcNow,
                Content = commentDto.Content,
                BlogId = commentDto.BlogId,
            };

            _dbContext.BlogComments.Add(comment);
            await _dbContext.SaveChangesAsync();

            int commentCount = await _dbContext.BlogComments
            .Where(bc => bc.BlogId == commentDto.BlogId)
            .CountAsync();

            return Ok(new CommentRespDto
            {
                Comment = comment.Content,
                CommentCount = commentCount,
                Author = comment.Author,
                PostedAt = comment.CreatedAt
            });

        }

        [HttpGet("getComments")]
        public async Task<IActionResult> GetCommentsForBlog([FromQuery] int blogid)
        {
            var comments = await _dbContext.BlogComments
                .Where(c => c.BlogId == blogid)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            if (comments == null)
            {
                return NotFound("No comments found for this blog");
            }

            return Ok(comments);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { message = "Search query cannot be empty" });
            }

            // Fetch data first, then process in memory
            var blogs = await _dbContext.Blogs
                .Where(b => b.Heading.Contains(query) || b.Content.Contains(query))
                .ToListAsync();

            var users = await _dbContext.Users
                .Where(u => u.Username.Contains(query) || u.FirstName.Contains(query) || u.LastName.Contains(query))
                .ToListAsync();

            // Now process and shape the data in memory
            var blogResults = blogs.Select(b => new
            {
                Id = b.Id,
                Name = b.Heading,
                Tagline = b.Tagline,
                Image = b.ImageUrls[0],
                Type = "blog"
            }).ToList();

            var userResults = users.Select(u => new
            {
                Id = u.Id,
                Username = u.Username,
                Name = u.FirstName + " " + u.LastName,
                ProfilePic = u.ProfilePictureUrl,
                Type = "user"
            }).ToList();

            return Ok(new { blogs = blogResults, users = userResults });
        }

        private IQueryable<BlogDTO> GetBlogQuery()
        {
            return _dbContext.Blogs.Select(blog => new BlogDTO
            {
                Id = blog.Id,
                Email = blog.UserEmail,
                Heading = blog.Heading,
                Tagline = blog.Tagline,
                Content = blog.Content,
                ImageUrls = blog.ImageUrls,
                Location = blog.Location,
                LikeCount = _dbContext.BlogLikes.Count(bl => bl.BlogId == blog.Id),
                Likes = _dbContext.BlogLikes.Where(bl => bl.BlogId == blog.Id).ToList(),
                ImagesMetaData = _dbContext.ImageMetadata.Where(im => im.BlogId == blog.Id).ToList(),
                CreatedAt = blog.CreatedAt,
                Username = _dbContext.Users
                    .Where(u => u.Email == blog.UserEmail)
                    .Select(u => u.Username)
                    .FirstOrDefault(),
                ProfilePicUrl = _dbContext.Users
                    .Where(u => u.Email == blog.UserEmail)
                    .Select(u => u.ProfilePictureUrl)
                    .FirstOrDefault(),
                LatestComment = _dbContext.BlogComments
                    .Where(u => u.BlogId == blog.Id)
                    .OrderByDescending(u => u.CreatedAt)
                    .FirstOrDefault(),
                CommentCount = _dbContext.BlogComments.Count(bc => bc.BlogId == blog.Id)
            });
        }
    }
}