namespace Ordena.Frontend.Services;

// Ending the app from a page.
public interface IAppLifetime
{
    // Closes Ordena as usual (the backup on exit included), then starts it again: how a new
    // language takes effect.
    void Restart();
}
