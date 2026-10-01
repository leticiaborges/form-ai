using FormAI.Application.AI;
using FormAI.Domain.Enums;

namespace FormAI.Infrastructure.AI;

public class FormAISchema
{
    public static object ResponseFormat(GenerationParameters parameters)
    {
        var types = (parameters.AllowedTypes is { Length: > 0 } ? parameters.AllowedTypes : Enum.GetValues<QuestionType>())
           .Select(t => t.ToString())
           .ToArray();

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "formai_schema",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "questions" },
                    properties = new
                    {
                        questions = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                additionalProperties = false,
                                required = new[] { "text", "type", "correctAnswer", "options" },
                                properties = new
                                {
                                    text = new { type = "string" },
                                    type = new { type = "string", @enum = types },
                                    correctAnswer = new { type = new[] { "string", "null" } },
                                    options = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            additionalProperties = false,
                                            required = new[] { "text", "isCorrect" },
                                            properties = new
                                            {
                                                text = new { type = "string" },
                                                isCorrect = new { type = new[] { "boolean", "null" } }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
