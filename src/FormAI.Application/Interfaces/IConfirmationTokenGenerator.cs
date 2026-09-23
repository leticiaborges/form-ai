namespace FormAI.Application.Interfaces;

public interface IConfirmationTokenGenerator
{
    (string RawToken, string TokenHash) Generate();
}
