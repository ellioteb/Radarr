﻿using System;
using System.Linq;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Queue;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.IndexerSearch
{
    public class MovieSearchService : IExecute<MoviesSearchCommand>, IExecute<MissingMoviesSearchCommand>
    {
        private readonly IMovieService _movieService;
        private readonly ISearchForNzb _nzbSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IQueueService _queueService;
        private readonly Logger _logger;

        public MovieSearchService(IMovieService movieService,
                                   ISearchForNzb nzbSearchService,
                                   IProcessDownloadDecisions processDownloadDecisions,
                                   IQueueService queueService,
                                   Logger logger)
        {
            _movieService = movieService;
            _nzbSearchService = nzbSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _queueService = queueService;
            _logger = logger;
        }

        private void SearchForMissingMovies(List<Movie> movies, bool userInvokedSearch)
        {
            _logger.ProgressInfo("Performing missing search for {0} movies", movies.Count);
            var downloadedCount = 0;

            foreach (var movieId in movies.GroupBy(e => e.Id))
            {
                List<DownloadDecision> decisions;

                try
                {
                    decisions = _nzbSearchService.MovieSearch(movieId.Key, userInvokedSearch);
                }
                catch (Exception ex)
                {
                    var message = String.Format("Unable to search for missing movie {0}", movieId.Key);
                    _logger.Error(ex, message);
                    continue;
                }

                var processed = _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed missing search for {0} movies. {1} reports downloaded.", movies.Count, downloadedCount);
        }

        public void Execute(MoviesSearchCommand message)
        {
            var downloadedCount = 0;
            foreach (var movieId in message.MovieIds)
            {
                var series = _movieService.GetMovie(movieId);
                
                if (!series.Monitored)
                {
                    _logger.Debug("Movie {0} is not monitored, skipping search", series.Title);
                }

                var decisions = _nzbSearchService.MovieSearch(movieId, false);//_nzbSearchService.SeasonSearch(message.MovieId, season.SeasonNumber, false, message.Trigger == CommandTrigger.Manual);
                downloadedCount += _processDownloadDecisions.ProcessDecisions(decisions).Grabbed.Count;

            }
            _logger.ProgressInfo("Movie search completed. {0} reports downloaded.", downloadedCount);
        }

        public void Execute(MissingMoviesSearchCommand message)
        {
            List<Movie> movies;

            movies = _movieService.MoviesWithoutFiles(new PagingSpec<Movie>
                                                            {
                                                                Page = 1,
                                                                PageSize = 100000,
                                                                SortDirection = SortDirection.Ascending,
                                                                SortKey = "Id",
                                                                FilterExpression = _movieService.ConstructFilterExpression(message.FilterKey, message.FilterValue)
                                                            }).Records.ToList();
        

        var queue = _queueService.GetQueue().Select(q => q.Movie.Id);
        var missing = movies.Where(e => !queue.Contains(e.Id)).ToList();

        SearchForMissingMovies(missing, message.Trigger == CommandTrigger.Manual);

        }

    }
}
