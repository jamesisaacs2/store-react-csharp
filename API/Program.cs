var builder = WebApplication.CreateBuilder(args);

// Add services to the container

builder.Services.AddControllers();

builder.Services.AddAutoMapper(typeof(MappingProfiles).Assembly);

builder.Services.AddSwaggerGen(config =>
{
   var c = config;

   c.SwaggerDoc("v1", new OpenApiInfo { Title = "API", Version = "v1" });
   c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
   {
      Description = "Jwt auth header",
      Name = "Authorization",
      In = ParameterLocation.Header,
      Type = SecuritySchemeType.ApiKey,
      Scheme = "Bearer"
   });

   c.AddSecurityRequirement(new OpenApiSecurityRequirement
   {
               {
                  new OpenApiSecurityScheme
                  {
                     Reference = new OpenApiReference
                     {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                     },
                     Scheme = "oauth2",
                     Name = "Bearer",
                     In = ParameterLocation.Header
                  },
                  new List<string>()
               }
   });
});

builder.Services.AddDbContext<StoreContext>(options =>
{
   var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

   string connStr;

   if (env == "Development")
   {
      // Use connection string from file.
      connStr = builder.Configuration.GetConnectionString("DefaultConnection");
   }
   else
   {
      // Use connection string provided at runtime by Heroku.
      var connUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

      // Parse connection URL to connection string for Npgsql
      connUrl = connUrl.Replace("postgres://", string.Empty);
      var pgUserPass = connUrl.Split("@")[0];
      var pgHostPortDb = connUrl.Split("@")[1];
      var pgHostPort = pgHostPortDb.Split("/")[0];
      var pgDb = pgHostPortDb.Split("/")[1];
      var pgUser = pgUserPass.Split(":")[0];
      var pgPass = pgUserPass.Split(":")[1];
      var pgHost = pgHostPort.Split(":")[0];
      var pgPort = pgHostPort.Split(":")[1];

      connStr = $"Server={pgHost};Port={pgPort};User Id={pgUser};Password={pgPass};Database={pgDb};SSL Mode=Require;Trust Server Certificate=true";
   }

   // Whether the connection string came from the local development configuration file
   // or from the environment variable from Heroku, use it to set up your DbContext.
   options.UseNpgsql(connStr);
});

builder.Services.AddCors();

builder.Services.AddIdentityCore<User>(options =>
{
   options.User.RequireUniqueEmail = true;
   options.Password.RequiredLength = 13;
})
   .AddRoles<Role>()
   .AddEntityFrameworkStores<StoreContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
   .AddJwtBearer(options =>
   {
      options.TokenValidationParameters = new TokenValidationParameters
      {
         ValidateIssuer = false,
         ValidateAudience = false,
         ValidateLifetime = true,
         ValidateIssuerSigningKey = true,
         IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8
            .GetBytes(builder.Configuration["JWTSettings:TokenKey"]))
      };
   });

builder.Services.AddAuthorization();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ImageService>();

// Add middleware

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (builder.Environment.IsDevelopment())
{
   app.UseSwagger();
   app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"));
}
else
{
   app.UseSwagger();
   app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1.1/swagger.json", "API v1.1"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

//CORS must go here:
app.UseCors(opt =>
{
   opt.AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithOrigins("http://localhost:3000");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToController("Index", "fallback", "Fallback");

using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<StoreContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

try
{
   await context.Database.MigrateAsync();
   await DbInitializer.Initialize(context, userManager);
}
catch (Exception ex)
{
   logger.LogError(ex, "Problem migrating data");
}

await app.RunAsync();

//paused converting global usings to .net6 at Entities