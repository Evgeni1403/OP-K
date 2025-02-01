using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ConsoleClient
{
	class Program
	{
		private static readonly HttpClient _client = new HttpClient();

		// Здесь храним текущий токен аутентификации
		private static string _token = string.Empty;

		static async Task Main(string[] args)
		{
			// Установите здесь URL-адрес вашего запущенного проекта 
			// (либо http://localhost:5296, либо http://localhost:<другой-порт>)
			_client.BaseAddress = new Uri("http://localhost:5210");

			// Основной цикл приложения
			while (true)
			{
				Console.WriteLine();
				// Если нет токена, показываем меню регистрации/логина
				if (string.IsNullOrEmpty(_token))
				{
					Console.WriteLine("=== Вы НЕ авторизованы! Доступные команды: ===");
					Console.WriteLine("1) register  - регистрация");
					Console.WriteLine("2) login     - вход (получить токен)");
					Console.WriteLine("0) exit      - выход");
				}
				else
				{
					Console.WriteLine("=== Вы АВТОРИЗОВАНЫ! Доступные команды: ===");
					Console.WriteLine("1) requests_history         - посмотреть историю запросов (GET)");
					Console.WriteLine("2) delete_requests_history  - удалить историю запросов (DELETE)");
					Console.WriteLine("3) change_password          - изменить пароль (и получить новый токен)");
					Console.WriteLine("4) add_text                 - добавить новый текст");
					Console.WriteLine("5) update_text              - изменить существующий текст");
					Console.WriteLine("6) delete_text              - удалить текст");
					Console.WriteLine("7) get_text                 - посмотреть один текст");
					Console.WriteLine("8) get_all_texts            - посмотреть все тексты");
					Console.WriteLine("9) encrypt                  - зашифровать (Double Transposition)");
					Console.WriteLine("10) decrypt                 - расшифровать (Double Transposition)");
					Console.WriteLine("11) logout                  - выйти из учётной записи (очистить токен)");
					Console.WriteLine("0) exit                     - завершить программу");
				}

				Console.Write(">> ");
				var command = Console.ReadLine()?.ToLower().Trim();
				if (command == "exit" || command == "0") break;

				try
				{
					// Если нет токена, обрабатываем только register/login
					if (string.IsNullOrEmpty(_token))
					{
						switch (command)
						{
							case "1":
							case "register":
								await RegisterUserAsync();
								break;
							case "2":
							case "login":
								await LoginAsync();
								break;
							default:
								Console.WriteLine("Неизвестная команда (нужно сначала зарегистрироваться / залогиниться).");
								break;
						}
					}
					else
					{
						// Есть токен, значит пользователь авторизован — доступно всё остальное
						switch (command)
						{
							case "1":
							case "requests_history":
								await GetRequestsHistoryAsync();
								break;
							case "2":
							case "delete_requests_history":
								await DeleteRequestsHistoryAsync();
								break;
							case "3":
							case "change_password":
								await ChangePasswordAsync();
								break;
							case "4":
							case "add_text":
								await AddTextAsync();
								break;
							case "5":
							case "update_text":
								await UpdateTextAsync();
								break;
							case "6":
							case "delete_text":
								await DeleteTextAsync();
								break;
							case "7":
							case "get_text":
								await GetOneTextAsync();
								break;
							case "8":
							case "get_all_texts":
								await GetAllTextsAsync();
								break;
							case "9":
							case "encrypt":
								await EncryptTextAsync();
								break;
							case "10":
							case "decrypt":
								await DecryptTextAsync();
								break;
							case "11":
							case "logout":
								Logout();
								break;
							default:
								Console.WriteLine("Неизвестная команда.");
								break;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("Ошибка: " + ex.Message);
				}
			}

			Console.WriteLine("Выход из программы...");
		}

		// ================================
		//   РЕГИСТРАЦИЯ / ЛОГИН / ЛОГАУТ
		// ================================
		private static async Task RegisterUserAsync()
		{
			Console.Write("Имя пользователя: ");
			string username = Console.ReadLine() ?? "";

			Console.Write("Пароль: ");
			string password = Console.ReadLine() ?? "";

			// Формируем URL: POST /register?username=...&password=...
			string url = $"/register?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";

			var response = await _client.PostAsync(url, content: null);
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				if (result != null && result.TryGetValue("token", out var newToken))
				{
					_token = newToken;
					Console.WriteLine("Регистрация прошла успешно!");
					Console.WriteLine("Ваш новый токен: " + _token);
				}
				else
				{
					Console.WriteLine("Регистрация успешна, но не удалось получить токен из ответа сервера.");
				}
			}
			else
			{
				Console.WriteLine("Ошибка при регистрации. Код статуса: " + response.StatusCode);
				Console.WriteLine("Текст ответа: " + await response.Content.ReadAsStringAsync());
			}
		}

		private static async Task LoginAsync()
		{
			Console.Write("Имя пользователя: ");
			string username = Console.ReadLine() ?? "";

			Console.Write("Пароль: ");
			string password = Console.ReadLine() ?? "";

			string url = $"/login?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";
			var response = await _client.PostAsync(url, null);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				if (result != null && result.TryGetValue("token", out var newToken))
				{
					_token = newToken;
					Console.WriteLine("Логин успешен!");
					Console.WriteLine("Ваш токен: " + _token);
				}
				else
				{
					Console.WriteLine("Логин успешен, но не удалось получить токен из ответа сервера.");
				}
			}
			else
			{
				Console.WriteLine("Ошибка при логине. Код статуса: " + response.StatusCode);
				Console.WriteLine("Текст ответа: " + await response.Content.ReadAsStringAsync());
			}
		}

		private static void Logout()
		{
			Console.WriteLine("Вы вышли из учётной записи. Токен сброшен.");
			_token = string.Empty;
		}

		// ================================
		//         ИСТОРИЯ ЗАПРОСОВ
		// ================================
		private static async Task GetRequestsHistoryAsync()
		{
			if (!CheckToken()) return;
			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			var response = await _client.GetAsync("/requests_history");
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("История запросов: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при получении истории запросов. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		private static async Task DeleteRequestsHistoryAsync()
		{
			if (!CheckToken()) return;
			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			var response = await _client.DeleteAsync("/requests_history");
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("История запросов удалена: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при удалении истории запросов. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// ================================
		//    СМЕНА ПАРОЛЯ (PATCH)
		// ================================
		private static async Task ChangePasswordAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Новый пароль: ");
			string newPassword = Console.ReadLine() ?? "";

			// PATCH /change_password?new_password=...
			string url = $"/change_password?new_password={Uri.EscapeDataString(newPassword)}";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			// Используем "PATCH". Для отправки patch в HttpClient придётся задать метод вручную.
			var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
			// Тело не нужно, так как передаем через query. Можно null.
			request.Content = null;

			var response = await _client.SendAsync(request);
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				if (dict != null && dict.TryGetValue("new_token", out var newToken))
				{
					_token = newToken;
					Console.WriteLine("Пароль успешно изменён, выдан новый токен: " + _token);
				}
				else
				{
					Console.WriteLine("Пароль изменён, но не удалось получить новый токен из ответа.");
				}
			}
			else
			{
				Console.WriteLine("Ошибка при смене пароля. Код статуса: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// ================================
		//   CRUD ДЛЯ TEXT
		// ================================
		// (5) Add text
		private static async Task AddTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите содержимое текста: ");
			string content = Console.ReadLine() ?? "";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/text?content={Uri.EscapeDataString(content)}";
			var response = await _client.PostAsync(url, null);

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Ответ сервера: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при добавлении текста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (6) Update text (PATCH /text/{text_id}?content=...)
		private static async Task UpdateTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID текста: ");
			string textIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(textIdStr, out int textId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			Console.Write("Новый контент: ");
			string newContent = Console.ReadLine() ?? "";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/text/{textId}?content={Uri.EscapeDataString(newContent)}";

			var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
			request.Content = null;

			var response = await _client.SendAsync(request);
			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Текст успешно обновлён: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при обновлении текста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (7) Delete text (DELETE /text/{text_id})
		private static async Task DeleteTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID текста, который хотите удалить: ");
			string textIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(textIdStr, out int textId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/text/{textId}";
			var response = await _client.DeleteAsync(url);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Текст удалён: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при удалении текста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (8) Get one text (GET /text/{text_id})
		private static async Task GetOneTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID текста: ");
			string textIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(textIdStr, out int textId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/text/{textId}";
			var response = await _client.GetAsync(url);
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Данные по тексту: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при получении текста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (9) Get all texts (GET /text)
		private static async Task GetAllTextsAsync()
		{
			if (!CheckToken()) return;

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = "/text";
			var response = await _client.GetAsync(url);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Список всех текстов: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при получении текстов. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// ================================
		//   ШИФРОВАНИЕ / ДЕШИФРОВАНИЕ
		// ================================
		// (10) Encrypt (POST /encrypt?text_id=...&rowKey=...&colKey=...)
		private static async Task EncryptTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID текста (text_id): ");
			string textIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(textIdStr, out int textId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			Console.Write("Введите rowKey: ");
			string rowKey = Console.ReadLine() ?? "";

			Console.Write("Введите colKey: ");
			string colKey = Console.ReadLine() ?? "";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/encrypt?text_id={textId}&rowKey={Uri.EscapeDataString(rowKey)}&colKey={Uri.EscapeDataString(colKey)}";
			var response = await _client.PostAsync(url, null);

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Зашифрованный текст: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при шифровании. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (11) Decrypt (POST /decrypt?text_id=...&rowKey=...&colKey=...)
		private static async Task DecryptTextAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID текста (text_id): ");
			string textIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(textIdStr, out int textId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			Console.Write("Введите rowKey: ");
			string rowKey = Console.ReadLine() ?? "";

			Console.Write("Введите colKey: ");
			string colKey = Console.ReadLine() ?? "";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/decrypt?text_id={textId}&rowKey={Uri.EscapeDataString(rowKey)}&colKey={Uri.EscapeDataString(colKey)}";
			var response = await _client.PostAsync(url, null);

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Расшифрованный текст: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при дешифровании. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// ================================
		//   ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
		// ================================
		private static bool CheckToken()
		{
			if (string.IsNullOrEmpty(_token))
			{
				Console.WriteLine("Ошибка: сначала необходимо авторизоваться.");
				return false;
			}
			return true;
		}
	}
}
