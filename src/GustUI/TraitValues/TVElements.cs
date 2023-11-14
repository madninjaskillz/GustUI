using System.Collections.Generic;
using GustUI.Elements;

namespace GustUI.TraitValues;

public class TVElements : TraitValue
{
    private List<Element> items = new List<Element>();
    public TVElements() 
    {
        items = new List<Element>();
    }

    public void Add(Element item) => items.Add(item);
    public void Remove(Element item) => items.Remove(item);
    public List<Element> Items => items;
}

