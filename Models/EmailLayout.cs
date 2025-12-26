namespace InvoiceApp.Models;

public enum EmailLayout
{
    Klar = 1,        // Default: Minimales Markup, Text-dominiert
    Strukturiert = 2, // Mit klaren Abschnitten und Trennlinien
    Betont = 3       // Mehr visuelle Führung mit farbigem Header
}
