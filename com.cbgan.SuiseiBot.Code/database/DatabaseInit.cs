﻿using Native.Sdk.Cqp.EventArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.cbgan.SuiseiBot.Code.database
{
    internal static class DatabaseInit//数据库初始化类
    {
        public static void Init(CQAppEnableEventArgs e)
        {
            string DBPath = System.IO.Directory.GetCurrentDirectory() + "\\data\\" + e.CQApi.GetLoginQQ() + "\\suisei.db";
            SQLiteHelper dbHelper = new SQLiteHelper(DBPath);
            if (!File.Exists(DBPath))//查找数据文件
            {
                dbHelper.CreateNewDBFile();
            }
            dbHelper.OpenDB();//打开数据库连接
            if (!dbHelper.TableExists("suisei")) //彗酱数据库初始化
            {
                e.CQLog.Info("DBInit", "suisei table not found\ncreate new table");
                dbHelper.CreateTable(SuiseiDBHandle.TableName, SuiseiDBHandle.ColName, SuiseiDBHandle.ColType,SuiseiDBHandle.PrimaryColName);
            }
            dbHelper.CloseDB();//关闭数据库连接
        }
    }
}