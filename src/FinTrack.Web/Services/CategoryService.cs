using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

public sealed class CategoryService(
    IDbContextFactory<AppDbContext> factory,
    ICategorizationService categorizer,
    INotificationService notifications,
    ILogger<CategoryService> logger) : ICategoryService
{
    public async Task<List<Category>> GetAllAsync(CategoryType? type = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Categories.AsNoTracking();
        if (type is CategoryType t) query = query.Where(c => c.Type == t);
        return await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();
    }

    public async Task<Result> SaveAsync(Category input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return Result.Fail("Enter a category name.");
        if (name.Length > 60) return Result.Fail("The name must be 60 characters or fewer.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Type == input.Type && c.Id != input.Id))
            return Result.Fail("This category already exists.");

        var keywords = string.Join(", ", input.KeywordList.Select(k => k.ToLowerInvariant()).Distinct());
        var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == input.Id);
        if (existing is null)
        {
            db.Categories.Add(new Category
            {
                Id = input.Id, Name = name, Type = input.Type, Color = input.Color, Icon = input.Icon, Keywords = keywords
            });
        }
        else
        {
            existing.Name = name;
            existing.Type = input.Type;
            existing.Color = input.Color;
            existing.Icon = input.Icon;
            existing.Keywords = keywords;
        }

        await db.SaveChangesAsync();
        RetrainInBackground();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return Result.Fail("Category not found.");

        var used = await db.Transactions.CountAsync(t => t.CategoryId == id);
        if (used > 0)
            return Result.Fail($"The category has {used} transactions. Move or delete them first.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        RetrainInBackground();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    private void RetrainInBackground() =>
        _ = Task.Run(async () =>
        {
            try { await categorizer.TrainAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Model retraining failed"); }
        });
}
