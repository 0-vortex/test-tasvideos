﻿using System.Linq;
using Microsoft.AspNetCore.Mvc;
using TASVideos.Data.Entity;
using TASVideos.Services;

namespace TASVideos.ViewComponents
{
	public class ListParents : ViewComponent
	{
		private readonly IWikiPages _wikiPages;

		public ListParents(IWikiPages wikiPages)
		{
			_wikiPages = wikiPages;
		}

		public IViewComponentResult Invoke(WikiPage pageData)
		{
			var subpages = _wikiPages.Query
					.ThatAreParentsOf(pageData.PageName)
					.ToList();

			return View(subpages);
		}
	}
}
