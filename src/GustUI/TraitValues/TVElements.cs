using System;
using System.Collections.Generic;
using System.Linq;
using GustUI.Elements;

namespace GustUI.TraitValues;

public class TVElements : TraitValue
{
    //private List<Element> items = new List<Element>();
    private List<Tuple<Element, string>> namedItems = null;
    public TVElements()
    {
        namedItems = new List<Tuple<Element, string>>();
    }

    // Depth-sorted view is cached and rebuilt only when membership or a child
    // Depth changes (Element.Depth setter calls InvalidateSort). Rebuilds
    // allocate a fresh list so callers holding the old one keep a valid snapshot.
    private List<Element> sortedCache;

    // Bumped on every structural change (add/remove/depth-reorder) —
    // Element.Draw's per-container visibility cull cache keys off this to
    // know when the child SET (not just positions) changed, alongside its
    // own position/size-driven invalidation (Element.MarkChildCullDirty).
    public int Version { get; private set; }

    public void InvalidateSort()
    {
        sortedCache = null;
        Version++;
    }

    public void Add(Element item, string name)
    {
        namedItems.Add(new(item, name));
        sortedCache = null;
        Version++;
        Log.This(name + " added to children, now " + namedItems.Count + " items");
    }
    public void Remove(Element item)
    {
        namedItems.Remove(namedItems.Find(x => x.Item1 == item));
        sortedCache = null;
        Version++;
    }
    public void Remove(string name)
    {
        namedItems.Remove(namedItems.Find(x => x.Item2 == name));
        sortedCache = null;
        Version++;
    }
    public List<Element> Items => sortedCache ??= namedItems.Select(x => x.Item1).OrderBy(x => x.Depth).ToList();
    public Element Get(string name)
    {
        var result = namedItems.FirstOrDefault(x => x.Item2 == name);
        if (result != null)
        {
            return result.Item1;
        }

        throw new Exception("Element not found : '" + name + "'");
    }

    public void DebugItems()
    {
        Log.This("Debugging items");
        foreach (var item in namedItems)
        {
            Log.This("Item: " + item.Item2 + " of type " + item.Item1 + "/" + item.Item1.GetType().Name);
        }
        Log.This("------------------");
    }
}

