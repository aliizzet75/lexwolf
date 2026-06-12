using System;

namespace LexWolf.Services
{
    // IFileReader-Schnittstelle für alle Dateitypen
    public interface IFileReader
    {
        string ReadFile(string filePath);
        bool CanHandle(string extension);
    }
}
