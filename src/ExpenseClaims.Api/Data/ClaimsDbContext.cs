using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
}
