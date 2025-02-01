using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Text;
using BCrypt.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// 1. Подключаем EF Core и SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
{
	options.UseSqlite("Data Source=vigenere.db");
});

// Для JSON-сериализации (при необходимости)
builder.Services.AddControllers().AddJsonOptions(opts =>
{
	opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// Подключаем генерацию документации Swagger (для удобства)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 2. Создаём/мигрируем базу данных (учебный пример — EnsureCreated)
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.EnsureCreated();
}

// Включаем Swagger в режиме Development
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// =======================
//  ВСПОМОГАТ. МЕТОДЫ ДЛЯ БД
// =======================
async Task<User?> GetUserByTokenAsync(AppDbContext db, string? token)
{
	if (string.IsNullOrWhiteSpace(token)) return null;
	return await db.Users.FirstOrDefaultAsync(u => u.Token == token);
}

void SaveRequestHistory(AppDbContext db, int userId, string endpoint)
{
	var record = new RequestHistory
	{
		UserId = userId,
		Endpoint = endpoint,
		Timestamp = DateTime.UtcNow
	};
	db.RequestsHistory.Add(record);
	db.SaveChanges();
}

static string? ExtractBearerToken(string authHeader)
{
	if (string.IsNullOrEmpty(authHeader)) return null;
	var match = Regex.Match(authHeader, @"Bearer\s+(.*)", RegexOptions.IgnoreCase);
	if (match.Success)
	{
		return match.Groups[1].Value.Trim();
	}
	return null;
}

// =======================
//         ЭНДПОИНТЫ
// =======================

// (0) Логин пользователя
app.MapPost("/login", async (AppDbContext db, string username, string password) =>
{
	if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
	{
		return Results.BadRequest(new { error = "Username and password required" });
	}

	var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
	if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
	{
		return Results.BadRequest(new { error = "Invalid username or password" });
	}

	user.Token = AuthUtils.GenerateToken();
	await db.SaveChangesAsync();

	return Results.Ok(new
	{
		message = "Login successful",
		token = user.Token
	});
});


// (1) Регистрация пользователя
app.MapPost("/register", async (AppDbContext db, string username, string password) =>
{
	if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
	{
		return Results.BadRequest(new { error = "Username and password required" });
	}

	var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
	if (existingUser != null)
	{
		return Results.BadRequest(new { error = "User already exists" });
	}

	string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);
	string newToken = AuthUtils.GenerateToken();

	var newUser = new User
	{
		Username = username,
		PasswordHash = hashedPassword,
		Token = newToken
	};

	db.Users.Add(newUser);
	await db.SaveChangesAsync();

	return Results.Created("/register", new
	{
		message = "User registered successfully",
		token = newToken
	});
});

// (2) История запросов пользователя
app.MapGet("/requests_history", async (AppDbContext db, HttpRequest req) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);

	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var history = await db.RequestsHistory
		.Where(r => r.UserId == user.Id)
		.OrderByDescending(r => r.Timestamp)
		.ToListAsync();

	SaveRequestHistory(db, user.Id, "GET /requests_history");

	return Results.Ok(new
	{
		history = history.Select(h => new
		{
			h.Id,
			h.Endpoint,
			h.Timestamp
		})
	});
});

// (3) Удалить историю запросов
app.MapDelete("/requests_history", async (AppDbContext db, HttpRequest req) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var userHistory = db.RequestsHistory.Where(r => r.UserId == user.Id);
	db.RequestsHistory.RemoveRange(userHistory);
	await db.SaveChangesAsync();

	SaveRequestHistory(db, user.Id, "DELETE /requests_history");

	return Results.Ok(new { message = "Request history deleted" });
});

// (4) Изменить пароль (должен менять и токен)
app.MapMethods("/change_password", new[] { "PATCH" }, async (AppDbContext db, HttpRequest req, string new_password) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	if (string.IsNullOrWhiteSpace(new_password))
	{
		return Results.BadRequest(new { error = "New password required" });
	}

	user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(new_password);
	user.Token = AuthUtils.GenerateToken();
	await db.SaveChangesAsync();

	SaveRequestHistory(db, user.Id, "PATCH /change_password");

	return Results.Ok(new
	{
		message = "Password changed successfully",
		new_token = user.Token
	});
});

// ===== CRUD для TextItem =====

// (5) Добавление текста
app.MapPost("/text", async (AppDbContext db, HttpRequest req, string content) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	if (string.IsNullOrWhiteSpace(content))
		return Results.BadRequest(new { error = "Content required" });

	var textItem = new TextItem
	{
		UserId = user.Id,
		Content = content
	};
	db.TextItems.Add(textItem);
	await db.SaveChangesAsync();

	SaveRequestHistory(db, user.Id, "POST /text");

	return Results.Created("/text", new
	{
		message = "Text added",
		text_id = textItem.Id
	});
});

// (6) Изменить текст
app.MapMethods("/text/{text_id}", new[] { "PATCH" }, async (AppDbContext db, HttpRequest req, int text_id, string content) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var textItem = await db.TextItems.FirstOrDefaultAsync(t => t.Id == text_id && t.UserId == user.Id);
	if (textItem == null)
		return Results.NotFound(new { error = "Text not found" });

	textItem.Content = content;
	await db.SaveChangesAsync();

	SaveRequestHistory(db, user.Id, $"PATCH /text/{text_id}");

	return Results.Ok(new { message = "Text updated" });
});

// (7) Удалить текст
app.MapDelete("/text/{text_id}", async (AppDbContext db, HttpRequest req, int text_id) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var textItem = await db.TextItems.FirstOrDefaultAsync(t => t.Id == text_id && t.UserId == user.Id);
	if (textItem == null)
		return Results.NotFound(new { error = "Text not found" });

	db.TextItems.Remove(textItem);
	await db.SaveChangesAsync();

	SaveRequestHistory(db, user.Id, $"DELETE /text/{text_id}");

	return Results.Ok(new { message = "Text deleted" });
});

// (8) Просмотреть один текст
app.MapGet("/text/{text_id}", async (AppDbContext db, HttpRequest req, int text_id) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var textItem = await db.TextItems.FirstOrDefaultAsync(t => t.Id == text_id && t.UserId == user.Id);
	if (textItem == null)
		return Results.NotFound(new { error = "Text not found" });

	SaveRequestHistory(db, user.Id, $"GET /text/{text_id}");

	return Results.Ok(new
	{
		textItem.Id,
		textItem.Content
	});
});

// (9) Просмотреть все тексты
app.MapGet("/text", async (AppDbContext db, HttpRequest req) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	var texts = await db.TextItems.Where(t => t.UserId == user.Id).ToListAsync();

	SaveRequestHistory(db, user.Id, "GET /text");

	var result = texts.Select(t => new { t.Id, t.Content });
	return Results.Ok(new { texts = result });
});

// =======================
//   ШИФРОВАНИЕ / ДЕШИФРОВАНИЕ
// =======================

// (10) Зашифровать текст (ТОЛЬКО text_id)
app.MapPost("/encrypt", async (AppDbContext db, HttpRequest req, int text_id, string rowKey, string colKey) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	if (string.IsNullOrWhiteSpace(rowKey) || string.IsNullOrWhiteSpace(colKey))
	{
		return Results.BadRequest(new { error = "Row and column keys are required" });
	}

	var textItem = await db.TextItems.FirstOrDefaultAsync(t => t.Id == text_id && t.UserId == user.Id);
	if (textItem == null)
	{
		return Results.NotFound(new { error = "Text not found" });
	}

	// Шифруем содержимое из БД
	string encrypted = DoubleTranspositionCipher.Encrypt(textItem.Content ?? "", rowKey, colKey);

	SaveRequestHistory(db, user.Id, "POST /encrypt");

	return Results.Ok(new { encrypted_text = encrypted });
});

// (11) Дешифровать текст (ТОЛЬКО text_id)
app.MapPost("/decrypt", async (AppDbContext db, HttpRequest req, int text_id, string rowKey, string colKey) =>
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	var user = await GetUserByTokenAsync(db, token);
	if (user == null) return Results.Unauthorized();

	if (string.IsNullOrWhiteSpace(rowKey) || string.IsNullOrWhiteSpace(colKey))
	{
		return Results.BadRequest(new { error = "Row and column keys are required" });
	}

	var textItem = await db.TextItems.FirstOrDefaultAsync(t => t.Id == text_id && t.UserId == user.Id);
	if (textItem == null)
	{
		return Results.NotFound(new { error = "Text not found" });
	}

	// Дешифруем содержимое из БД
	string decrypted = DoubleTranspositionCipher.Decrypt(textItem.Content ?? "", rowKey, colKey);

	SaveRequestHistory(db, user.Id, "POST /decrypt");

	return Results.Ok(new { decrypted_text = decrypted });
});

// Запуск приложения
app.Run();

// =======================
//      МОДЕЛИ и КОНТЕКСТ
// =======================
public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

	public DbSet<User> Users => Set<User>();
	public DbSet<RequestHistory> RequestsHistory => Set<RequestHistory>();
	public DbSet<TextItem> TextItems => Set<TextItem>();
}

public class User
{
	public int Id { get; set; }
	public string? Username { get; set; }
	public string? PasswordHash { get; set; }
	public string? Token { get; set; }
}

public class RequestHistory
{
	public int Id { get; set; }
	public int UserId { get; set; }
	public string? Endpoint { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class TextItem
{
	public int Id { get; set; }
	public int UserId { get; set; }
	public string? Content { get; set; }
}

// =======================
//  ШИФР ДВОЙНОЙ ПЕРЕСТАНОВКИ
// =======================
static class DoubleTranspositionCipher
{
	public static string Encrypt(string plaintext, string rowKey, string colKey)
	{
		// Преобразуем ключи в массивы индексов
		var rowKeyOrder = GetKeyOrder(rowKey);
		var colKeyOrder = GetKeyOrder(colKey);

		// Разбиваем текст на строки и столбцы
		var rows = rowKeyOrder.Length;
		var cols = colKeyOrder.Length;
		var matrix = new char[rows, cols];

		// Заполняем матрицу
		int index = 0;
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				if (index < plaintext.Length)
				{
					matrix[i, j] = plaintext[index++];
				}
				else
				{
					matrix[i, j] = ' '; // Заполняем пробелами, если текст закончился
				}
			}
		}

		// Переставляем строки
		var rowPermutedMatrix = new char[rows, cols];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				rowPermutedMatrix[i, j] = matrix[rowKeyOrder[i], j];
			}
		}

		// Переставляем столбцы
		var colPermutedMatrix = new char[rows, cols];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				colPermutedMatrix[i, j] = rowPermutedMatrix[i, colKeyOrder[j]];
			}
		}

		// Собираем зашифрованный текст
		var encryptedText = new StringBuilder();
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				encryptedText.Append(colPermutedMatrix[i, j]);
			}
		}

		return encryptedText.ToString();
	}

	public static string Decrypt(string ciphertext, string rowKey, string colKey)
	{
		// Преобразуем ключи в массивы индексов
		var rowKeyOrder = GetKeyOrder(rowKey);
		var colKeyOrder = GetKeyOrder(colKey);

		// Разбиваем текст на строки и столбцы
		var rows = rowKeyOrder.Length;
		var cols = colKeyOrder.Length;
		var matrix = new char[rows, cols];

		// Заполняем матрицу
		int index = 0;
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				if (index < ciphertext.Length)
				{
					matrix[i, j] = ciphertext[index++];
				}
				else
				{
					matrix[i, j] = ' '; // Заполняем пробелами, если текст закончился
				}
			}
		}

		// Восстанавливаем столбцы
		var colRestoredMatrix = new char[rows, cols];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				colRestoredMatrix[i, colKeyOrder[j]] = matrix[i, j];
			}
		}

		// Восстанавливаем строки
		var rowRestoredMatrix = new char[rows, cols];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				rowRestoredMatrix[rowKeyOrder[i], j] = colRestoredMatrix[i, j];
			}
		}

		// Собираем расшифрованный текст
		var decryptedText = new StringBuilder();
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				decryptedText.Append(rowRestoredMatrix[i, j]);
			}
		}

		return decryptedText.ToString().TrimEnd();
	}

	private static int[] GetKeyOrder(string key)
	{
		// Преобразуем ключ в массив индексов, указывающих порядок перестановки
		var keyChars = key.ToCharArray();
		var keyOrder = keyChars.Select((c, i) => new { Char = c, Index = i })
							   .OrderBy(x => x.Char)
							   .Select(x => x.Index)
							   .ToArray();
		return keyOrder;
	}
}

static class AuthUtils
{
	public static string GenerateToken()
	{
		return Guid.NewGuid().ToString("N");
	}
}