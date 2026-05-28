using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Alexa.Services;

/// <summary>
/// Хранит токен авторизации в зашифрованном виде (DPAPI)
/// </summary>
public static class AuthTokenStorage
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Alexa.AuthToken.v1");

    /// <summary>
    /// Возвращает каталог настроек приложения
    /// </summary>
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Alexa");

    /// <summary>
    /// Возвращает путь к DPAPI-файлу токена
    /// </summary>
    private static string TokenPath => Path.Combine(SettingsDirectory, "auth-token.dpapi");

    /// <summary>
    /// Загружает и расшифровывает токен пользователя
    /// </summary>
    public static string Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(TokenPath))
            return string.Empty;

        try
        {
            var protectedBytes = File.ReadAllBytes(TokenPath);
            var tokenBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(tokenBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Шифрует и сохраняет токен через DPAPI или удаляет файл, если токен пустой
    /// </summary>
    public static void Save(string token)
    {
        if (!OperatingSystem.IsWindows())
            return;

        token = token.Trim();
        Directory.CreateDirectory(SettingsDirectory);

        if (string.IsNullOrWhiteSpace(token))
        {
            Delete();
            return;
        }

        // CurrentUser привязывает секрет к пользователю
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var protectedBytes = ProtectedData.Protect(
            tokenBytes,
            Entropy,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(TokenPath, protectedBytes);
    }

    /// <summary>
    /// Удаляет файл с зашифрованным токеном
    /// </summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(TokenPath))
                File.Delete(TokenPath);
        }
        catch
        {
            // Ошибка удаления токена не должна ломать сброс остальных настроек
        }
    }
}
