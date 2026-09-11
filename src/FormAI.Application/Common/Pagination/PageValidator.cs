namespace FormAI.Application.Common.Pagination;

public static class PageValidator
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) GetPageSize(int page, int pageSize, int maxPageSize = MaxPageSize)
    {
        return (Math.Max(1, page), Math.Clamp(pageSize, 1, maxPageSize));
    }
}