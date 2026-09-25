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
    /// <summary>Changes the name <paramref name="item"/> is held under, in
    /// place — same position, no add/remove. For an element whose name
    /// follows what it is showing (a window whose own view has left it), so
    /// <see cref="Get"/> and <see cref="Remove(string)"/> agree with its
    /// ElementName afterwards.</summary>
    public void Rename(Element item, string name)
    {
        int index = namedItems.FindIndex(x => x.Item1 == item);
        if (index >= 0)
        {
            namedItems[index] = new(item, name);
        }
    }

    /// <summary>Draw and hit-test order: Depth, then — among siblings at the
    /// same Depth — whichever was brought forward (<see cref="Element.MoveToFront"/>)
    /// most recently, then insertion order. Floating windows all clamp to one
    /// ceiling, so without the second key a click could not change which of
    /// them is on top (ezmuze #301).</summary>
    public List<Element> Items => sortedCache ??= namedItems.Select(x => x.Item1)
        .OrderBy(x => x.Depth).ThenBy(x => x.FrontSequence).ToList();
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

