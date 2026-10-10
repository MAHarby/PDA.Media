using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes TV episodes (see <see cref="DataService{TEntity}"/>). An episode is the same as an existing one
/// when the show, season and episode number match; for an episode whose number couldn't be read (0), the name is
/// compared instead. Lists are in season and episode order.
/// </summary>
public class TVShowEpisodeService : DataService<TVShowEpisode>
{
    public TVShowEpisodeService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public TVShowEpisodeService(IDbContextFactory<DataContext> contextFactory, ILogger<TVShowEpisodeService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<TVShowEpisode, bool>> IsSameAs(TVShowEpisode record) => record.EpisodeNo > 0
        ? e => e.TVShowId == record.TVShowId && e.SeasonNo == record.SeasonNo && e.EpisodeNo == record.EpisodeNo
        : e => e.TVShowId == record.TVShowId && e.SeasonNo == record.SeasonNo && e.EpisodeNo == 0 && e.Name == record.Name;

    protected override IOrderedQueryable<TVShowEpisode> DefaultOrder(IQueryable<TVShowEpisode> query)
        => query.OrderBy(e => e.TVShowId).ThenBy(e => e.SeasonNo).ThenBy(e => e.EpisodeNo).ThenBy(e => e.Name);

    public TVShowEpisode? GetRecordByEpisode(int tvShowId, int seasonNo, int episodeNo)
        => First(e => e.TVShowId == tvShowId && e.SeasonNo == seasonNo && e.EpisodeNo == episodeNo);

    public Task<TVShowEpisode?> GetRecordByEpisodeAsync(int tvShowId, int seasonNo, int episodeNo, CancellationToken cancellationToken = default)
        => FirstAsync(e => e.TVShowId == tvShowId && e.SeasonNo == seasonNo && e.EpisodeNo == episodeNo, cancellationToken);

    public List<TVShowEpisode> GetAllRecordsByTVShowId(int tvShowId)
        => List(q => DefaultOrder(q.Where(e => e.TVShowId == tvShowId)));

    public Task<List<TVShowEpisode>> GetAllRecordsByTVShowIdAsync(int tvShowId, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(e => e.TVShowId == tvShowId)), cancellationToken);

    public List<TVShowEpisode> GetAllRecordsBySeason(int tvShowId, int seasonNo)
        => List(q => DefaultOrder(q.Where(e => e.TVShowId == tvShowId && e.SeasonNo == seasonNo)));

    public Task<List<TVShowEpisode>> GetAllRecordsBySeasonAsync(int tvShowId, int seasonNo, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(e => e.TVShowId == tvShowId && e.SeasonNo == seasonNo)), cancellationToken);
}
