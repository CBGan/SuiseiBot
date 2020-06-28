﻿using com.cbgan.SuiseiBot.Code.Resource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.cbgan.SuiseiBot.Resource
{
    internal class ChatKeywords
    {
        /// <summary>
        /// 关键字初始化及存储
        /// </summary>
        public static Dictionary<string, KeywordType> KeyWords = new Dictionary<string, KeywordType>();
        public static void KeywordInit()
        {
            //1 娱乐功能
            KeyWords.Add(".r", KeywordType.SurpriseMFK);//随机数
            KeyWords.Add("给老子来个禁言套餐", KeywordType.SurpriseMFK);
            KeyWords.Add("请问可以告诉我你的年龄吗？", KeywordType.SurpriseMFK);
            KeyWords.Add("给爷来个优质睡眠套餐", KeywordType.SurpriseMFK);
            //2 奇奇怪怪的签到
            KeyWords.Add("彗酱今天也很可爱", KeywordType.Suisei);

            KeyWords.Add("debug", KeywordType.Debug);
        }
    }
}
