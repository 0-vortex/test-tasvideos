﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Legacy.Data.Site;

namespace TASVideos.Legacy.Imports
{
    public class PublicationImporter
    {
		public static void Import(
			string connectionStr,
			ApplicationDbContext context,
			NesVideosSiteContext legacySiteContext)
		{
			// TODO
			// multiple streaming url links
			// multiple archive links
			var publications = new List<Publication>();
			var publicationAuthors = new List<PublicationAuthor>();
			var publicationFiles = new List<PublicationFile>();
			var publicationTags = new List<PublicationTag>();

			var legacyMovies = legacySiteContext.Movies
				.Include(m => m.MovieFiles)
				.ThenInclude(mf => mf.Storage)
				.Include(m => m.MovieClasses)
				.Include(m => m.Publisher)
				.Where(m => m.Id > 0)
				.ToList();

			var legacyClassTypes = legacySiteContext.ClassTypes.ToList();

			var publicationWikis = context.WikiPages
				.ThatAreNotDeleted()
				.WithNoChildren()
				.Where(w => w.PageName.StartsWith(LinkConstants.PublicationWikiPage))
				.Select(s => new { s.Id, s.PageName })
				.ToList();

			var submissions = context.Submissions
				.Select(s => new
				{
					s.Id,
					s.SystemFrameRateId,
					s.Frames,
					s.RerecordCount,
					s.GameId,
					Authors = s.SubmissionAuthors.Select(sa => sa.Author),
					s.AdditionalAuthors,
					s.System,
					s.SystemFrameRate
				})
				.ToList();

			var games = context.Games.ToList();
			var tags = context.Tags.Select(t => new { t.Id, t.DisplayName }).ToList();

			var movieTypes = new[] { "B2", "BK", "C", "6", "2", "S", "B", "L", "W", "3", "Y", "G", "#", "F", "Q", "E", "Z", "X", "U", "I", "R", "8", "4", "9", "7", "F3", "MA", "LT" };
			var torrentTypes = new[] { "M", "N", "O", "P", "T" };

			var pubs = (from lm in legacyMovies
				join w in publicationWikis on LinkConstants.PublicationWikiPage + lm.Id equals w.PageName
				join s in submissions on lm.SubmissionId equals s.Id
				join g in games on s.GameId ?? -1 equals g.Id
				select new
				{
					Movie = lm,
					Wiki = w,
					Sub = s,
					Game = g,
				})
				.ToList();

			foreach (var pub in pubs)
			{
				var movieFiles = pub.Movie.MovieFiles.Where(f => movieTypes.Contains(f.Type)).ToList();
				var mainMovieFile = movieFiles.First(); // Pick the first one to be the official, we have no better way really
				var screenshotUrl = pub.Movie.MovieFiles.First(f => f.Type == "H");
				var torrentUrls = pub.Movie.MovieFiles.Where(f => torrentTypes.Contains(f.Type));
				var mirror = pub.Movie.MovieFiles.FirstOrDefault(f => f.Type == "A")?.FileName;
				var streaming = (pub.Movie.MovieFiles.FirstOrDefault(f => f.Type == "J" && f.FileName.Contains("youtube"))
					?? pub.Movie.MovieFiles.FirstOrDefault(f => f.Type == "J"))?.FileName;

				var publication = new Publication
				{
					Id = pub.Movie.Id,
					WikiContentId = pub.Wiki.Id,
					SubmissionId = pub.Movie.SubmissionId,
					TierId = pub.Movie.Tier,
					CreateUserName = pub.Movie.Publisher.Name ?? "Unknown",
					CreateTimeStamp = ImportHelper.UnixTimeStampToDateTime(pub.Movie.PublishedDate),
					LastUpdateTimeStamp = ImportHelper.UnixTimeStampToDateTime(pub.Movie.LastChange),
					ObsoletedById = pub.Movie.ObsoletedBy == -1 ? null : pub.Movie.ObsoletedBy,
					Frames = pub.Sub.Frames,
					RerecordCount = pub.Sub.RerecordCount,
					RomId = -1, // Place holder
					GameId = pub.Sub.GameId ?? -1,
					Game = pub.Game,
					MovieFile = mainMovieFile.Storage.FileData,
					MovieFileName = mainMovieFile.FileName,
					SystemFrameRateId = pub.Sub.SystemFrameRateId ?? 0,
					SystemFrameRate = pub.Sub.SystemFrameRate,
					SystemId = pub.Movie.SystemId,
					System = pub.Sub.System,
					Branch = pub.Movie.Branch.NullIfWhiteSpace(),
					MirrorSiteUrl = mirror,
					OnlineWatchingUrl = streaming,
					AdditionalAuthors = pub.Sub.AdditionalAuthors
				};

				var pubAuthors = pub.Sub.Authors
					.Select(u => new PublicationAuthor
					{
						UserId = u.Id,
						Author = u,
						PublicationId = pub.Movie.Id,
						Publication = publication,
					})
					.ToList();

				publicationAuthors.AddRange(pubAuthors);

				foreach (var author in pubAuthors)
				{
					publication.Authors.Add(author);
				}

				publication.GenerateTitle();
				publications.Add(publication);

				publicationFiles.Add(new PublicationFile
				{
					PublicationId = pub.Movie.Id,
					Type = FileType.Screenshot,
					Path = screenshotUrl.FileName,
					CreateTimeStamp = DateTime.UtcNow,
					LastUpdateTimeStamp = DateTime.UtcNow,
					Description = screenshotUrl.Description.NullIfWhiteSpace()
				});

				publicationFiles.AddRange(torrentUrls.Select(t => new PublicationFile
				{
					PublicationId = pub.Movie.Id,
					Type = FileType.Torrent,
					Path = t.FileName,
					CreateTimeStamp = DateTime.UtcNow,
					LastUpdateTimeStamp = DateTime.UtcNow
				}));

				publicationFiles.AddRange(movieFiles.Skip(1).Select(m => new PublicationFile
				{
					PublicationId = pub.Movie.Id,
					Type = FileType.MovieFile,
					Path = m.FileName,
					FileData = m.Storage.FileData,
					Description = m.FileName.ToLower().Contains("consoleverified")
						? "Console Verication"
						: "Converted to " + Path.GetExtension(m.FileName),
					CreateTimeStamp = DateTime.UtcNow,
					LastUpdateTimeStamp = DateTime.UtcNow
				}));

				foreach (var mc in pub.Movie.MovieClasses)
				{
					var classType = mc.ClassId >= 1000
						? legacyClassTypes.Single(c => c.Id == mc.ClassId)
						: legacyClassTypes.Single(c => c.OldId == mc.ClassId);

					if (classType.PositiveText.Contains("Genre"))
					{
						continue;
					}

					var tag = mc.Value == 1
						? tags.Single(t => t.DisplayName == classType.PositiveText)
						: tags.Single(t => t.DisplayName == classType.NegativeText);

					publicationTags.Add(new PublicationTag
					{
						PublicationId = pub.Movie.Id,
						TagId = tag.Id
					});
				}
			}

			var pubColumns = new[]
			{
				nameof(Publication.Branch),
				nameof(Publication.WikiContentId),
				nameof(Publication.Id),
				nameof(Publication.SubmissionId),
				nameof(Publication.TierId),
				nameof(Publication.CreateUserName),
				nameof(Publication.CreateTimeStamp),
				nameof(Publication.LastUpdateTimeStamp),
				nameof(Publication.Frames),
				nameof(Publication.RerecordCount),
				nameof(Publication.GameId),
				nameof(Publication.RomId),
				nameof(Publication.MovieFile),
				nameof(Publication.MovieFileName),
				nameof(Publication.SystemFrameRateId),
				nameof(Publication.SystemId),
				nameof(Publication.Title),
				nameof(Publication.MirrorSiteUrl),
				nameof(Publication.OnlineWatchingUrl),
				nameof(Publication.ObsoletedById),
				nameof(Publication.AdditionalAuthors)
			};

			publications.BulkInsert(connectionStr, pubColumns, nameof(ApplicationDbContext.Publications), bulkCopyTimeout: 600);

			var pubAuthorColumns = new[]
			{
				nameof(PublicationAuthor.UserId),
				nameof(PublicationAuthor.PublicationId)
			};

			publicationAuthors.BulkInsert(connectionStr, pubAuthorColumns, nameof(ApplicationDbContext.PublicationAuthors), SqlBulkCopyOptions.Default);

			var pubFileColumns = new[]
			{
				nameof(PublicationFile.PublicationId),
				nameof(PublicationFile.Path),
				nameof(PublicationFile.Type),
				nameof(PublicationFile.Description),
				nameof(PublicationFile.FileData),
				nameof(PublicationFile.CreateUserName),
				nameof(PublicationFile.LastUpdateUserName),
				nameof(PublicationFile.CreateTimeStamp),
				nameof(PublicationFile.LastUpdateTimeStamp)
			};

			publicationFiles.BulkInsert(connectionStr, pubFileColumns, nameof(ApplicationDbContext.PublicationFiles), SqlBulkCopyOptions.Default, bulkCopyTimeout: 600);

			var pubTagColumns = new[]
			{
				nameof(PublicationTag.PublicationId),
				nameof(PublicationTag.TagId)
			};

			publicationTags.BulkInsert(connectionStr, pubTagColumns, nameof(ApplicationDbContext.PublicationTags), SqlBulkCopyOptions.Default);
		}
	}
}
