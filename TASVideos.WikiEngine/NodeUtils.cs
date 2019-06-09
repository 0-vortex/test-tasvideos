using System;
using System.Collections.Generic;
using System.Linq;

namespace TASVideos.WikiEngine.AST
{
	public class InternalLinkInfo
	{
		public string Link { get; set; }
		public string Excerpt { get; set; }
	}

	public static class NodeUtils
	{
		public static void Replace(IList<INode> input, Func<INode, bool> predicate, Func<INode, INode> transform)
		{
			for (var i = 0; i < input.Count; i++)
			{
				if (predicate(input[i]))
				{
					input[i] = transform(input[i]);
				}
				var cc = input[i] as INodeWithChildren;
				if (cc != null)
					Replace(cc.Children, predicate, transform);
			}
		}

		public static IEnumerable<INode> Find(IEnumerable<INode> input, Func<INode, bool> predicate)
		{
			foreach (var n in input)
			{
				if (predicate(n))
					yield return n;
				var cc = n as INodeWithChildren;
				if (cc != null)
				{
					foreach (var c in Find(cc.Children, predicate))
						yield return c;
				}
			}
		}

		private static void ForEach(IEnumerable<INode> input, Action<INode> callback)
		{
			foreach (var n in input)
			{
				callback(n);
				if (n is INodeWithChildren cc)
					ForEach(cc.Children, callback);
			}			
		}

		public static List<InternalLinkInfo> GetAllInternalLinks(string content, IEnumerable<INode> input)
		{
			var ret = new List<InternalLinkInfo>();
			Action<string, INode> addLink = (string link, INode node) =>
			{
				if (link.StartsWith("/Users/Profile/", StringComparison.OrdinalIgnoreCase))
				{
					// [user:foo] from the wiki isn't supposed to be reported as a link for referrals.
					// It's rendered as a link, which is fine.
					// The fact that it's already turned into an <a href> before we get to this method is
					// a slightly regrettable implementation detail of wiki parsing, and if they were reported,
					// it'd be a leaky abstraction leaking.

					// [=Users/Profile/foo] gets excluded from reporting because of this; minor casualty.
					return;
				}
				var si = Math.Max(node.CharStart - 20, 0);
				var se = Math.Min(node.CharEnd + 20, content.Length);
				ret.Add(new InternalLinkInfo
				{
					// for purposes of html markup, all <a class=intlink> have hrefs that start with a leading '/'
					// this is how the wiki syntax expects them to work.
					// But in the context of counting internal referrers, that leading slash is not needed or wanted, so strip it here
					Link = link.TrimStart('/'),
					Excerpt = content.Substring(si, se - si)
				});
			};
			NodeUtils.ForEach(input, node =>
			{
				if (node is Module m)
				{
					if (m.Text.StartsWith("__wikiLink|"))
					{
						var link = m.Text.Split('|')[1];
						addLink(link, node);
					}
				}
				else if (node is Element e)
				{
					if (e.Tag == "a" && e.Attributes.TryGetValue("class", out var clazz) && clazz == "intlink")
					{
						var link = e.Attributes["href"];
						addLink(link, node);
					}
				}
			});
			return ret;
		}
	}
}
