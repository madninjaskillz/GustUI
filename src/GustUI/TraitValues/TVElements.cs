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

    public void Add(Element item, string name)
    {
        namedItems.Add(new(item, name));
        Log.This(name + " added to children, now " + namedItems.Count + " items");
    }
    public void Remove(Element item) => namedItems.Remove(namedItems.Find(x => x.Item1 == item));
    public void Remove(string name) => namedItems.Remove(namedItems.Find(x => x.Item2 == name));
    public List<Element> Items => namedItems.Select(x => x.Item1).ToList();
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

