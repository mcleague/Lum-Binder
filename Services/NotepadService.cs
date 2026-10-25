using System;
using System.IO;
using Serilog;

namespace BinderApp.Services
{
    public sealed class NotepadService
    {
        private readonly ConfigService _configService;

        public NotepadService(ConfigService configService)
        {
            _configService = configService;
        }

        private string FilePath => Path.Combine(_configService.ConfigDir, "notepad.txt");

        public string Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return File.ReadAllText(FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка чтения notepad.txt");
            }

            return "Личные заметки для RP.";
        }

        public void Save(string content)
        {
            try
            {
                File.WriteAllText(FilePath, content ?? string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка сохранения notepad.txt");
            }
        }
    }
}
