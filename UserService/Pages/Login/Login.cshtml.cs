using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace YourWebApp.Pages.Account
{
    [IgnoreAntiforgeryToken]
    public class LoginModel : PageModel // Removed IgnoreAntiforgeryToken attribute
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LoginModel> _logger;

        // Properties to pass data to the Razor page for client-side script
        public string? JwtTokenForClient { get; private set; }
        public string? UsernameForClient { get; private set; }
        public bool LoginSuccess { get; private set; } = false;

        public LoginModel(HttpClient httpClient, IConfiguration configuration, ILogger<LoginModel> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        [BindProperty]
        public LoginInputModel Input { get; set; } = new LoginInputModel();

        [TempData]
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            LoginSuccess = false;
            JwtTokenForClient = null;
            UsernameForClient = null;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInformation("OnPostAsync entered. Username from Input: {UsernameValue}, Password Is Empty: {PasswordIsEmpty}",
                                  Input.Username, string.IsNullOrEmpty(Input.Password));

            LoginSuccess = false;
            JwtTokenForClient = null;
            UsernameForClient = null;

            // Ensure form data is correctly mapped
            if (string.IsNullOrEmpty(Input.Username) || string.IsNullOrEmpty(Input.Password))
            {
                _logger.LogWarning("OnPostAsync: Username or password is empty.");
                ModelState.AddModelError(string.Empty, "Indtast venligst både brugernavn og adgangskode.");
                return Page();
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("OnPostAsync: ModelState is invalid.");
                foreach (var modelStateKey in ModelState.Keys)
                {
                    var modelStateVal = ModelState[modelStateKey];
                    foreach (var error in modelStateVal.Errors)
                    {
                        _logger.LogWarning("Validation Error - Key: {Key}, Error: {ErrorMsg}", modelStateKey, error.ErrorMessage);
                    }
                }
                ErrorMessage = "Indtast venligst både brugernavn og adgangskode.";
                return Page();
            }

            var authServiceBaseUrl = _configuration["AuthServiceUrl"];
            if (string.IsNullOrEmpty(authServiceBaseUrl))
            {
                _logger.LogError("AuthServiceUrl is not configured in IConfiguration.");
                ErrorMessage = "Server configuration error (auth endpoint).";
                return Page();
            }

            var loginApiUrl = $"{authServiceBaseUrl.TrimEnd('/')}/Auth/login";

            try
            {
                _logger.LogInformation("Attempting to login user {Username} via API: {ApiUrl}", Input.Username, loginApiUrl);

                // Create the payload
                var authControllerPayload = new AuthControllerLoginInput
                {
                    Username = Input.Username,
                    Password = Input.Password
                };

                // Log the payload before sending (exclude password for security)
                _logger.LogInformation("Sending login payload for user: {Username}", Input.Username);

                // Send the API request
                var response = await _httpClient.PostAsJsonAsync(loginApiUrl, authControllerPayload);

                // Process the response
                if (response.IsSuccessStatusCode)
                {
                    var loginResponse = await response.Content.ReadFromJsonAsync<LoginApiResponse>();
                    if (loginResponse != null && !string.IsNullOrEmpty(loginResponse.Token))
                    {
                        _logger.LogInformation("Login successful for user {Username}. Token received.", Input.Username);

                        JwtTokenForClient = loginResponse.Token;
                        UsernameForClient = Input.Username;
                        LoginSuccess = true;
                        ErrorMessage = null;
                        Input.Password = string.Empty; // Clear password for security

                        _logger.LogInformation("PageModel state before returning Page(): LoginSuccess={LoginSuccessState}, JwtTokenForClient is set: {IsTokenSet}",
                                             LoginSuccess, !string.IsNullOrEmpty(JwtTokenForClient));
                        return Page();
                    }
                    else
                    {
                        _logger.LogWarning("Login API call successful for {Username} but no token was returned.", Input.Username);
                        ErrorMessage = "Login lykkedes, men token blev ikke modtaget.";
                    }
                }
                else
                {
                    // Log the status code
                    _logger.LogWarning("Login failed. HTTP Status: {StatusCode}", response.StatusCode);

                    // Try to get error message from response
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Error response content: {ErrorContent}", errorContent);

                    // Try to deserialize error message if possible
                    try
                    {
                        var errorData = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
                        ErrorMessage = errorData?.Message ?? "Ugyldigt brugernavn eller adgangskode.";
                    }
                    catch
                    {
                        ErrorMessage = "Ugyldigt brugernavn eller adgangskode.";
                    }

                    _logger.LogWarning("Login API call failed for {Username}. Status: {StatusCode}. Message: {ErrorMessage}",
                                     Input.Username, response.StatusCode, ErrorMessage);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HttpRequestException during login for {Username} to {ApiUrl}. Check AuthService.",
                               Input.Username, loginApiUrl);
                ErrorMessage = "Netværksfejl under login. Tjenesten kan være utilgængelig.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General exception during login for {Username}.", Input.Username);
                ErrorMessage = "En uventet fejl opstod.";
            }

            return Page();
        }
    }

    // DTOs (Input model, API request/response models)
    public class LoginInputModel
    {
        [Required(ErrorMessage = "Brugernavn er påkrævet.")]
        [Display(Name = "Brugernavn")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Adgangskode er påkrævet.")]
        [DataType(DataType.Password)]
        [Display(Name = "Adgangskode")]
        public string Password { get; set; } = string.Empty;
    }

    public class AuthControllerLoginInput
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginApiResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    public class ApiErrorResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}