using Microsoft.EntityFrameworkCore;

namespace Dispatch.Server.Data;

public class DispatchDatabaseContext(DbContextOptions<DispatchDatabaseContext> options) : DbContext(options);