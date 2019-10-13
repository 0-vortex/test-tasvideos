﻿using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TASVideos.Data.Entity;
using TASVideos.Services;

namespace TASVideos.Pages.Wiki
{
	[AllowAnonymous]
	[IgnoreAntiforgeryToken]
	public class PreviewModel : BasePageModel
	{
		private readonly IWikiPages _pages;

		public PreviewModel(IWikiPages pages)
		{
			_pages = pages;
		}

		public string Markup { get; set; } = "";

		[FromQuery]
		public int? Id { get; set; }

		public WikiPage PageData { get; set; } = new WikiPage();

		public async Task<IActionResult> OnPost()
		{
			Markup = new StreamReader(Request.Body, Encoding.UTF8).ReadToEnd();
			if (Id.HasValue)
			{
				PageData = await _pages.Revision(Id.Value);
			}

			return Page();
		}
	}
}
