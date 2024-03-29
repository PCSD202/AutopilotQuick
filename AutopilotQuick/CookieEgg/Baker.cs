﻿#region

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace AutopilotQuick.CookieEgg;

public static class Baker
{
    public record struct WeightedCookie(string FileName, int Weight);

    private static readonly List<WeightedCookie> Cookies = new List<WeightedCookie>()
    {
        new("Apple", 100),
        new("Blue_Swirl", 100),
        new("Cherry_Jam", 100),
        new("chocolate_chip", 150),
        new("Ghost", 50),
        new("Golden", 10),
        new("M_and_M", 100),
        new("Pink_Swirl", 100),
        new("Sugar_Cookie", 100)
    };

    private static Random _rnd = new Random();
    
    
    public static WeightedCookie SurpriseMe()
    {
        var totalWeight = Cookies.Sum(x => x.Weight);
        var randomNumber = _rnd.Next(0, totalWeight);
        
        foreach (var cookie in Cookies)
        {
            if (randomNumber < cookie.Weight)
            {
                return cookie;
            }

            randomNumber = randomNumber - cookie.Weight;
        }

        return Cookies.First();
    }
}