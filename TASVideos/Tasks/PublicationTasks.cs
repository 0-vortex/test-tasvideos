﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AutoMapper;

using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Constants;
using TASVideos.Data.Entity;
using TASVideos.Models;
using TASVideos.Services;
using TASVideos.ViewComponents;

namespace TASVideos.Tasks
{
	public class PublicationTasks
	{
		private readonly ApplicationDbContext _db;
		private readonly ICacheService _cache;
		private readonly IMapper _mapper;
		private readonly IWikiService _wikiService;
		
		public PublicationTasks(
			ApplicationDbContext db,
			ICacheService cache,
			IMapper mapper,
			IWikiService wikiService)
		{
			_db = db;
			_cache = cache;
			_mapper = mapper;
			_wikiService = wikiService;
		}

		/// <summary>
		/// Gets all the possible values that can be tokens in the Movies- url
		/// </summary>
		public async Task<PublicationSearchModel> GetMovieTokenData()
		{
			var cacheKey = $"{nameof(PublicationTasks)}{nameof(GetMovieTokenData)}";
			if (_cache.TryGetValue(cacheKey, out PublicationSearchModel cachedResult))
			{
				return cachedResult;
			}

			using (await _db.Database.BeginTransactionAsync())
			{
				var result = new PublicationSearchModel
				{
					Tiers = await _db.Tiers.Select(t => t.Name.ToLower()).ToListAsync(),
					SystemCodes = await _db.GameSystems.Select(s => s.Code.ToLower()).ToListAsync(),
					Tags = await _db.Tags.Select(t => t.Code.ToLower()).ToListAsync(),
					Genres = await _db.Genres.Select(g => g.DisplayName.ToLower()).ToListAsync(),
					Flags = await _db.Flags.Select(f => f.Token.ToLower()).ToListAsync()
				};

				_cache.Set(cacheKey, result);

				return result;
			}
		}

		/// <summary>
		/// Gets a publication with the given <see cref="id" /> for the purpose of display
		/// If no publication with the given id is found then null is returned
		/// </summary>
		public async Task<PublicationViewModel> GetPublicationForDisplay(int id)
		{
			var publication =  await _db.Publications
				.Select(p => new PublicationViewModel
				{
					Id = p.Id,
					CreateTimeStamp = p.CreateTimeStamp,
					LastUpdateTimeStamp = p.LastUpdateTimeStamp,
					LastUpdateUser = p.LastUpdateUserName,
					Title = p.Title,
					OnlineWatchingUrl = p.OnlineWatchingUrl,
					MirrorSiteUrl = p.MirrorSiteUrl,
					ObsoletedBy = p.ObsoletedById,
					MovieFileName = p.MovieFileName,
					SubmissionId = p.SubmissionId,
					TierIconPath = p.Tier.IconPath,
					// ReSharper disable once PossibleLossOfFraction
					RatingCount = p.PublicationRatings.Count / 2,
					Files = p.Files
						.Select(f => new PublicationViewModel.FileModel
						{
							Path = f.Path,
							Type = f.Type
						})
						.ToList(),
					Tags = p.PublicationTags
						.Select(pt => new PublicationViewModel.TagModel
						{
							DisplayName = pt.Tag.DisplayName,
							Code = pt.Tag.Code
						})
						.ToList(),
					GenreTags = p.Game.GameGenres
						.Select(gg => new PublicationViewModel.TagModel
						{
							DisplayName = gg.Genre.DisplayName,
							Code = gg.Genre.DisplayName // TODO
						}),
					Flags = p.PublicationFlags
						.Where(pf => pf.Flag.IconPath != null)
						.Select(pf => new PublicationViewModel.FlagModel
						{
							IconPath = pf.Flag.IconPath,
							LinkPath = pf.Flag.LinkPath,
							Name = pf.Flag.Name
						})
						.ToList()
				})
				.SingleOrDefaultAsync(p => p.Id == id);

			if (publication != null)
			{
				var pageName = LinkConstants.SubmissionWikiPage + publication.SubmissionId;
				publication.TopicId = (await _db.ForumTopics
					.SingleOrDefaultAsync(t => t.PageName == pageName))
					?.Id ?? 0;
			}

			return publication;
		}

		/// <summary>
		/// Returns a list of potential "interesting" movies
		/// so that one may be randomly picked as a suggested movie
		/// Intended for the front page, for newcomers to the site
		/// </summary>
		public async Task<IEnumerable<int>> FrontPageMovieCandidates(string tier, string flags)
		{
			var query = _db.Publications
				.ThatAreCurrent()
				.AsQueryable();

			if (!string.IsNullOrWhiteSpace(tier))
			{
				query = query.Where(p => p.Tier.Name == tier);
			}

			if (!string.IsNullOrWhiteSpace(flags))
			{
				var flagsArr = flags.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
				query = query.Where(p => p.PublicationFlags.Any(pf => flagsArr.Contains(pf.Flag.Token)));
			}

			return await query
				.Select(p => p.Id)
				.ToListAsync();
		}

		/// <summary>
		/// Gets publication data for the DisplayMiniMovie module
		/// </summary>
		public async Task<MiniMovieModel> GetPublicationMiniMovie(int id)
		{
			if (id != 0)
			{
				return await _db.Publications
					.Select(p => new MiniMovieModel
					{
						Id = p.Id,
						Title = p.Title,
						Screenshot = p.Files.First(f => f.Type == FileType.Screenshot).Path,
						OnlineWatchingUrl = p.OnlineWatchingUrl,
					})
					.SingleOrDefaultAsync(p => p.Id == id);
			}
			else
			{
				return await _db.Publications
					.Select(p => new MiniMovieModel
					{
						Id = 0,
						Title = "Error",
						Screenshot = p.Files.FirstOrDefault(f => f.Type == FileType.Screenshot).Path,
						OnlineWatchingUrl = p.OnlineWatchingUrl,
					})
					.SingleAsync(p => p.Id == id);
			}
		}

		/// <summary>
		/// Gets the title of a movie with the given id
		/// If the movie is not found, null is returned
		/// </summary>
		public async Task<string> GetTitle(int id)
		{
			return (await _db.Publications
				.Select(s => new { s.Id, s.Title })
				.SingleOrDefaultAsync(s => s.Id == id))?.Title;
		}

		/// <summary>
		/// Returns the publication file as bytes with the given id
		/// If no publication is found, an empty byte array is returned
		/// </summary>
		public async Task<(byte[], string)> GetPublicationMovieFile(int id)
		{
			var data = await _db.Publications
				.Where(s => s.Id == id)
				.Select(s => new { s.MovieFile, s.MovieFileName })
				.SingleOrDefaultAsync();

			if (data == null)
			{
				return (new byte[0], "");
			}

			return (data.MovieFile, data.MovieFileName);
		}

		// TODO: paging
		/// <summary>
		/// Returns a list of publications with the given <see cref="searchCriteria" />
		/// for the purpose of displaying on a movie listings page
		/// </summary>
		public async Task<IEnumerable<PublicationViewModel>> GetMovieList(PublicationSearchModel searchCriteria)
		{
			var query = _db.Publications
				.AsQueryable();

			if (searchCriteria.MovieIds.Any())
			{
				query = query.Where(p => searchCriteria.MovieIds.Contains(p.Id));
			}
			else
			{
				if (searchCriteria.SystemCodes.Any())
				{
					query = query.Where(p => searchCriteria.SystemCodes.Contains(p.System.Code));
				}

				if (searchCriteria.Tiers.Any())
				{
					query = query.Where(p => searchCriteria.Tiers.Contains(p.Tier.Name));
				}

				if (!searchCriteria.ShowObsoleted)
				{
					query = query.ThatAreCurrent();
				}

				if (searchCriteria.Years.Any())
				{
					query = query.Where(p => searchCriteria.Years.Contains(p.CreateTimeStamp.Year));
				}

				if (searchCriteria.Tags.Any())
				{
					query = query.Where(p => p.PublicationTags.Any(t => searchCriteria.Tags.Contains(t.Tag.Code)));
				}

				if (searchCriteria.Genres.Any())
				{
					query = query.Where(p => p.Game.GameGenres.Any(gg => searchCriteria.Genres.Contains(gg.Genre.DisplayName)));
				}

				if (searchCriteria.Flags.Any())
				{
					query = query.Where(p => p.PublicationFlags.Any(f => searchCriteria.Flags.Contains(f.Flag.Token)));
				}

				if (searchCriteria.Authors.Any())
				{
					query = query.Where(p => p.Authors.Select(a => a.UserId).Any(a => searchCriteria.Authors.Contains(a)));
				}
			}

			// TODO: automapper, single movie is the same logic
			return await query
				.OrderBy(p => p.System.Code)
				.ThenBy(p => p.Game.DisplayName)
				.Select(p => new PublicationViewModel
				{
					Id = p.Id,
					CreateTimeStamp = p.CreateTimeStamp,
					Title = p.Title,
					OnlineWatchingUrl = p.OnlineWatchingUrl,
					MirrorSiteUrl = p.MirrorSiteUrl,
					ObsoletedBy = p.ObsoletedById,
					MovieFileName = p.MovieFileName,
					SubmissionId = p.SubmissionId,
					RatingCount = p.PublicationRatings.Count / 2,
					TierIconPath = p.Tier.IconPath,
					Files = p.Files.Select(f => new PublicationViewModel.FileModel
					{
						Path = f.Path,
						Type = f.Type
					}).ToList(),
					Tags = p.PublicationTags
						.Select(pt => new PublicationViewModel.TagModel
						{
							DisplayName = pt.Tag.DisplayName,
							Code = pt.Tag.Code
						})
						.ToList(),
					GenreTags = p.Game.GameGenres
						.Select(gg => new PublicationViewModel.TagModel
						{
							DisplayName = gg.Genre.DisplayName,
							Code = gg.Genre.DisplayName // TODO
						})
						.ToList(),
					Flags = p.PublicationFlags
						.Where(pf => pf.Flag.IconPath != null)
						.Select(pf => new PublicationViewModel.FlagModel
						{
							IconPath = pf.Flag.IconPath,
							LinkPath = pf.Flag.LinkPath,
							Name = pf.Flag.Name
						})
						.ToList()
				})
				.ToListAsync();
		}

		/// <summary>
		/// Returns a list of publications with the given <see cref="searchCriteria" />
		/// in a brief table form
		/// </summary>
		public async Task<IEnumerable<TabularMovieListResultModel>> GetTabularMovieList(TabularMovieListSearchModel searchCriteria)
		{
			// It is important to actually query for an Entity object here instead of a ViewModel
			// Because we need the title property which is a derived property that can't be done in Linq to Sql
			// And needs a variety of information from sub-tables, hence all the includes
			var movies = await _db.Publications
				.Include(p => p.Tier)
				.Include(p => p.Game)
				.Include(p => p.System)
				.Include(p => p.SystemFrameRate)
				.Include(p => p.Files)
				.Include(p => p.Authors)
				.ThenInclude(pa => pa.Author)
				.Where(p => searchCriteria.Tiers.Contains(p.Tier.Name))
				.ByMostRecent()
				.Take(searchCriteria.Limit)
				.ToListAsync();

			var results = movies
				.Select(m => new TabularMovieListResultModel
				{
					Id = m.Id,
					CreateTimeStamp = m.CreateTimeStamp,
					Time = m.Time,
					Game = m.Game.DisplayName,
					Authors = string.Join(", ", m.Authors.Select(pa => pa.Author)),
					ObsoletedBy = null, // TODO: previous logic
					Screenshot = m.Files.First(f => f.Type == FileType.Screenshot).Path
				})
				.ToList();

			return results;
		}

		/// <summary>
		/// Gets a <see cref="Publication"/> with the given <see cref="id" /> for the purpose of editing
		/// If no publication with the given id is found then null is returned
		/// </summary>
		public async Task<PublicationEditModel> GetPublicationForEdit(int id, IEnumerable<PermissionTo> userPermissions)
		{
			using (await _db.Database.BeginTransactionAsync())
			{
				var model = await _db.Publications
					.Select(p => new PublicationEditModel
					{
						Id = p.Id,
						Tier = p.Tier.Name,
						TierIconPath = p.Tier.IconPath,
						TierLink = p.Tier.Link,
						SystemCode = p.System.Code,
						Title = p.Title,
						ObsoletedBy = p.ObsoletedById,
						Branch = p.Branch,
						EmulatorVersion = p.EmulatorVersion,
						OnlineWatchingUrl = p.OnlineWatchingUrl,
						MirrorSiteUrl = p.MirrorSiteUrl,
						SelectedFlags = p.PublicationFlags
							.Select(pf => pf.FlagId)
							.ToList(),
						SelectedTags = p.PublicationTags
							.Select(pt => pt.TagId)
							.ToList(),
						Markup = p.WikiContent.Markup
					})
					.SingleOrDefaultAsync(p => p.Id == id);

				model.AvailableMoviesForObsoletedBy =
					await GetAvailableMoviesForObsoletedBy(id, model.SystemCode);

				model.AvailableFlags = await GetAvailableFlags(userPermissions);
				model.AvailableTags = await GetAvailableTags();

				return model;
			}
		}

		// TODO: document
		public async Task<IEnumerable<SelectListItem>> GetAvailableFlags(IEnumerable<PermissionTo> userPermissions)
		{
			return await _db.Flags
				.Select(f => new SelectListItem
				{
					Text = f.Name,
					Value = f.Id.ToString(),
					Disabled = f.PermissionRestriction.HasValue
						&& !userPermissions.Contains(f.PermissionRestriction.Value)
				})
				.ToListAsync();
		}

		public async Task<IEnumerable<SelectListItem>> GetAvailableTags()
		{
			return await _db.Tags
				.Select(f => new SelectListItem
				{
					Text = f.DisplayName,
					Value = f.Id.ToString(),
				})
				.ToListAsync();
		}

		// TODO: document
		public async Task<IEnumerable<SelectListItem>> GetAvailableMoviesForObsoletedBy(int id, string systemCode)
		{
			return await _db.Publications
				.ThatAreCurrent()
				.Where(p => p.System.Code == systemCode)
				.Where(p => p.Id != id)
				.Select(p => new SelectListItem
				{
					Text = p.Title,
					Value = p.Id.ToString()
				})
				.ToListAsync();
		}

		// TODO: document
		public async Task UpdatePublication(PublicationEditModel model)
		{
			var publication = await _db.Publications
				.Include(p => p.WikiContent)
				.Include(p => p.System)
				.Include(p => p.SystemFrameRate)
				.Include(p => p.Game)
				.Include(p => p.Authors)
				.ThenInclude(pa => pa.Author)
				.SingleOrDefaultAsync(p => p.Id == model.Id);

			if (publication != null)
			{
				publication.Branch = model.Branch;
				publication.ObsoletedById = model.ObsoletedBy;
				publication.EmulatorVersion = model.EmulatorVersion;
				publication.OnlineWatchingUrl = model.OnlineWatchingUrl;
				publication.MirrorSiteUrl = model.MirrorSiteUrl;

				publication.GenerateTitle();

				publication.PublicationFlags.Clear();
				_db.PublicationFlags.RemoveRange(
					_db.PublicationFlags.Where(pf => pf.PublicationId == publication.Id));

				foreach (var flag in model.SelectedFlags)
				{
					publication.PublicationFlags.Add(new PublicationFlag
					{
						PublicationId = publication.Id,
						FlagId = flag
					});
				}

				publication.PublicationTags.Clear();
				_db.PublicationTags.RemoveRange(
					_db.PublicationTags.Where(pt => pt.PublicationId == publication.Id));

				foreach (var tag in model.SelectedTags)
				{
					publication.PublicationTags.Add(new PublicationTag
					{
						PublicationId = publication.Id,
						TagId = tag
					});
				}

				await _db.SaveChangesAsync();

				if (model.Markup != publication.WikiContent.Markup)
				{
					var revision = new WikiPage
					{
						PageName = $"{LinkConstants.PublicationWikiPage}{model.Id}",
						Markup = model.Markup,
						MinorEdit = model.MinorEdit,
						RevisionMessage = model.RevisionMessage,
					};
					await _wikiService.Add(revision);

					publication.WikiContentId = revision.Id;
				}
			}
		}

		/// <summary>
		/// Returns the <see cref="Publication"/> with the given <see cref="id"/>
		/// for the purpose of setting <see cref="TASVideos.Data.Entity.Game.Game"/> cataloging information.
		/// If no publication is found, null is returned
		/// </summary>
		public async Task<PublicationCatalogModel> Catalog(int id)
		{
			using (_db.Database.BeginTransactionAsync())
			{
				var model = await _db.Publications
					.Select(s => new PublicationCatalogModel
					{
						Id = s.Id,
						RomId = s.RomId,
						GameId = s.GameId,
						SystemId = s.SystemId,
						SystemFrameRateId = s.SystemFrameRateId,
					})
					.SingleAsync(s => s.Id == id);

				if (model == null)
				{
					return null;
				}

				await PopulateCatalogDropDowns(model);
				return model;
			}
		}

		public async Task PopulateCatalogDropDowns(PublicationCatalogModel model)
		{
			using (_db.Database.BeginTransactionAsync())
			{
				model.AvailableRoms = await _db.Roms
					.Where(r => r.GameId == model.GameId)
					.Where(r => r.Game.SystemId == model.SystemId)
					.Select(r => new SelectListItem
					{
						Value = r.Id.ToString(),
						Text = r.Name
					})
					.ToListAsync();

				model.AvailableGames = await _db.Games
					.Where(g => g.SystemId == model.SystemId)
					.Select(g => new SelectListItem
					{
						Value = g.Id.ToString(),
						Text = g.GoodName
					})
					.ToListAsync();

				model.AvailableSystems = await _db.GameSystems
					.Select(s => new SelectListItem
					{
						Value = s.Id.ToString(),
						Text = s.Code
					})
					.ToListAsync();

				model.AvailableSystemFrameRates = await _db.GameSystemFrameRates
					.Where(sf => sf.GameSystemId == model.SystemId)
					.Select(sf => new SelectListItem
					{
						Value = sf.Id.ToString(),
						Text = sf.RegionCode + " (" + sf.FrameRate + ")"
					})
					.ToListAsync();
			}
		}

		/// <summary>
		/// Updates the given <see cref="Publication"/> with the given <see cref="TASVideos.Data.Entity.Game.Game"/> catalog information
		/// </summary>
		public async Task UpdateCatalog(PublicationCatalogModel model)
		{
			var publication = await _db.Publications.SingleAsync(s => s.Id == model.Id);
			_mapper.Map(model, publication);
			await _db.SaveChangesAsync();
		}

		public async Task<IEnumerable<AuthorListEntry>> GetPublishedAuthorList()
		{
			return await _db.Users
				.Where(u => u.Publications.Any())
				.Select(u => new AuthorListEntry
				{
					Id = u.Id,
					UserName = u.UserName,
					ActivePublicationCount = u.Publications.Count(pa => !pa.Publication.ObsoletedById.HasValue),
					ObsoletePublicationCount = u.Publications.Count(pa => pa.Publication.ObsoletedById.HasValue)
				})
				.ToListAsync();
		}

		// TODO: document
		public async Task<PublicationTierEditModel> GetTiersForEdit(int publicationId)
		{
			var model = await _db.Publications
				.Where(p => p.Id == publicationId)
				.Select(p => new PublicationTierEditModel
				{
					Id = p.Id,
					Title = p.Title,
					TierId = p.TierId 
				})
				.SingleOrDefaultAsync();

			if (model != null)
			{
				model.AvailableTiers = await _db.Tiers
					.Select(t => new SelectListItem
					{
						Text = t.Name,
						Value = t.Id.ToString()
					})
					.ToListAsync();
			}

			return model;
		}

		// TODO: document
		public async Task<bool> UpdateTier(int publicationId, int tierId)
		{
			var publication = await _db.Publications
				.SingleOrDefaultAsync(p => p.Id == publicationId);

			if (publication == null)
			{
				return false;
			}

			var tier = await _db.Tiers.SingleOrDefaultAsync(t => t.Id == tierId);
			if (tier == null)
			{
				return false;
			}

			publication.TierId = tierId;
			await _db.SaveChangesAsync();
			return true;
		}
	}
}
