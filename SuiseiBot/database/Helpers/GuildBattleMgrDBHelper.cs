using System.Collections.Generic;
using System.Linq;
using Native.Sdk.Cqp.EventArgs;
using SqlSugar;
using SuiseiBot.Code.Resource.TypeEnum;
using SuiseiBot.Code.SqliteTool;
using SuiseiBot.Code.Tool;
using SuiseiBot.Code.Tool.LogUtils;

namespace SuiseiBot.Code.Database.Helpers
{
    internal class GuildBattleMgrDBHelper
    {
        private long   GroupId { get; set; }
        private string DBPath  { get; set; }

        private string TableName { get; set; }

        public GuildBattleMgrDBHelper(object sender, CQGroupMessageEventArgs eventArgs)
        {
            GroupId   = eventArgs.FromGroup.Id;
            DBPath    = SugarUtils.GetDBPath(eventArgs.CQApi.GetLoginQQ().Id.ToString());
            TableName = $"{SugarTableUtils.GetTableName<GuildBattle>()}_{GroupId}";
        }

        /// <summary>
        /// 检查公会是否存在
        /// </summary>
        public bool GuildExists()
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            return dbClient.Queryable<GuildData>().Where(guild => guild.Gid == GroupId).Any();
        }
        
        /// <summary>
        /// 开始会战
        /// </summary>
        /// <returns>0：开始成功 | -1：上次仍未结束或已经开始</returns>
        public int StartBattle()
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            if (SugarUtils.TableExists<GuildBattle>(dbClient, TableName))
            {
                ConsoleLog.Error("会战管理数据库", "会战表已经存在，请检查是否未结束上次会战统计");
                return -1;
            }
            else
            {
                SugarUtils.CreateTable<GuildBattle>(dbClient, TableName);
                ConsoleLog.Info("会战管理数据库", "开始新的一期会战统计");
                return 0;
            }
        }

        /// <summary>
        /// 结束会战
        /// </summary>
        /// <returns>0：成功结束 | 1：还未开始会战</returns>
        public int EndBattle()
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            if (SugarUtils.TableExists<GuildBattle>(dbClient, TableName))
            {
                ConsoleLog.Error("会战管理数据库", "结束一期会战，开始输出数据");
                //TODO: EXCEL导出公会战数据
                return 0;
            }
            else
            {
                ConsoleLog.Info("会战管理数据库", "会战表为空，请确认是否已经开始会战统计");
                return -1;
            }
        }

        /// <summary>
        /// 出刀命令
        /// </summary>
        /// <param name="uid">用户QQ号</param>
        /// <param name="dmg">当前刀伤害</param>
        /// <param name="attackType">当前刀类型（0=通常刀 1=尾刀 2=补偿刀 3=掉刀）</param>
        /// <returns>0：正常 | -1：该成员不存在 | -2：需要先下树 | -3：未开始出刀 | -4：会战未开始 | -99：数据库出错</returns>
        public int Attack(int uid, long dmg, int attackType)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            var data = dbClient.Queryable<MemberStatus>()
                               .Where(i => i.Uid == uid && i.Gid == GroupId)
                               .ToList();
            if (data.Any())
            {
                switch (data.First().Flag)
                {
                    //当前并未开始出刀，请先申请出刀=>返回
                    case 0:
                        return -3;
                    //进入出刀判断
                    case 1:
                        break;
                    //需要下树才能报刀
                    case 3:
                        return -2;
                }

                //出刀判断

                //当前BOSS数据
                GuildBattleStatus bossStatus =
                    dbClient.Queryable<GuildBattleStatus>()
                            .InSingle(GroupId);//单主键查询
                if (bossStatus == null)
                {
                    return -4;
                }

                long CurrHP = bossStatus.HP;


                long realDamage     = dmg;
                //是否需要切换boss
                bool needChangeBoss = false;
                if (dmg >= CurrHP)
                {
                    realDamage     = CurrHP;
                    needChangeBoss = true;
                }

                //储存请求的时间
                long requestTime = data.First().Time;

                //插入一刀数据
                var insertData = new GuildBattle()
                {
                    Uid    = uid,
                    Time   = requestTime,
                    BossID = GetCurrentBossID(bossStatus),
                    Damage = realDamage,
                    Flag   = attackType
                };
                bool succInsert = dbClient.Insertable<GuildBattle>(insertData)
                                          .AS(TableName)
                                          .ExecuteCommand() > 0;
                //如果是尾刀
                if (attackType == 1)
                {
                    //全部下树，出刀中取消出刀状态
                    dbClient.Updateable(new MemberStatus() {Flag = 0})
                            .Where(i => i.Flag == 3 || i.Flag == 1)
                            .UpdateColumns(i => new {i.Flag})
                            .ExecuteCommand();
                    //TODO: 预约中取消BOSS预约
                }

                //更新成员信息，报刀后变空闲
                var memberStatus = new MemberStatus()
                {
                    Flag = 0,
                    Info = "",
                    Time = Utils.GetNowTimeStamp,
                };
                bool succUpdate = dbClient.Updateable(memberStatus)
                                          .ExecuteCommandHasChange();

                return succUpdate && succInsert ? 0 : -99;
            }
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// SL命令
        /// </summary>
        /// <param name="uid">成员QQ号</param>
        /// <returns>0：正常 | -1：成员不存在 | -2：当日已用过SL | -3：当前并不在出刀状态中 | -99：数据库出错</returns>
        public int SL(int uid)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            var currSL =
                dbClient.Queryable<MemberStatus>()
                        .Where(i => i.Uid == uid && i.Gid == GroupId)
                        .ToList();
            if (currSL.Any())
            {
                if (currSL.FirstOrDefault()?.SL == 1)
                {
                    return -2;
                }

                if (currSL.FirstOrDefault()?.Flag != 1)
                {
                    return -3;
                }

                return dbClient
                       .Updateable(new MemberStatus() {Flag = 0, SL = Utils.GetNowTimeStamp})
                       .UpdateColumns(i => new {i.Flag, i.SL})
                       .ExecuteCommandHasChange()
                    ? 0
                    : -99;
            }
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// 撤销SL命令
        /// </summary>
        /// <param name="uid">成员QQ号</param>
        /// <returns>0：正常 | -1：成员不存在 | -2：今天未使用过SL | -99：数据库出错</returns>
        public int SLUndo(int uid)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            var currSL =
                dbClient.Queryable<MemberStatus>()
                        .Where(i => i.Uid == uid && i.Gid == GroupId)
                        .ToList();
            if (currSL.Any())
            {
                if (currSL.FirstOrDefault()?.SL == 0)
                {
                    return -2;
                }

                return dbClient.Updateable(new MemberStatus() {SL = 0})
                               .UpdateColumns(i => new {i.SL})
                               .ExecuteCommandHasChange()
                    ? 0
                    : -99;
            }
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// 申请出刀
        /// </summary>
        /// <param name="uid">成员QQ号（请填写真实造成伤害的成员的QQ号）</param>
        /// <param name="flag">当前成员状态的Flag</param>
        /// <returns>0：正常 | -1：成员不存在 | -2：宁不是搁着树上爬吗，出个🔨的刀 | -3：已出满3刀 | -4：已经出刀，请不要重复出刀 | -99：数据库出错</returns>
        public int RequestAttack(int uid, out int flag)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            var member =
                dbClient.Queryable<MemberStatus>()
                        .Where(i => i.Uid == uid && i.Gid == GroupId)
                        .ToList();
            //成员是否存在 
            if (member.Any())
            {
                //当前成员状态是否能出刀
                flag = member.FirstOrDefault().Flag;
                switch (member.FirstOrDefault()?.Flag)
                {
                    //空闲可以出刀
                    case 0:
                        break;
                    //重复出刀
                    case 1:
                        return -4;
                    //挂树不允许出刀
                    case 3:
                        return -2;
                }

                //出刀数判断
                var AttackHistory =
                    dbClient.Queryable<GuildBattle>()
                            .AS(TableName)
                            //今天零点之后出刀的
                            .Where(i => i.Uid == uid && i.Time > Utils.GetTodayStamp)
                            .GroupBy(i => i.Uid)
                            //筛选出刀总数
                            .Select(i => new {id = i.Uid, times = SqlFunc.AggregateCount(i.Uid)}).ToList();
                //一天只能3刀
                if (AttackHistory.Any() && AttackHistory.FirstOrDefault()?.times >= 3)
                {
                    return -3;
                }

                //TODO: 获取当前BOSS ID 写入info中
                //修改出刀成员状态
                return dbClient.Updateable(new MemberStatus() {Flag = 1})
                               .UpdateColumns(i => new {i.Flag})
                               .Where(i => i.Uid == uid && i.Gid == GroupId)
                               .ExecuteCommandHasChange()
                    ? 0
                    : -99;
            }
            else
            {
                flag = -1;
                return -1;
            }
        }

        /// <summary>
        /// 删刀
        /// </summary>
        /// <param name="AttackId">出刀编号</param>
        /// <param name="IsBossChanged">BOSS是否已经变更</param>
        /// <returns>0：正常 | -1：未找到该出刀编号 | -99：数据库出错</returns>
        public int DeleteAttack(int AttackId, out bool IsBossChanged)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);
            var attackInfo =
                dbClient.Queryable<GuildBattle>()
                        .AS(TableName)
                        .Where(i => i.Bid == AttackId)
                        .ToList();
            if (attackInfo.Any())
            {
                bool succDelete = dbClient.Deleteable<GuildBattle>()
                                          .AS(TableName)
                                          .Where(i => i.Bid == AttackId)
                                          .ExecuteCommandHasChange();
                //TODO: 重新计算boss的血量，并判断是否为当前boss
                if (false /*BOSS变更条件*/)
                {
                    IsBossChanged = true;
                }
                else
                {
                    IsBossChanged = false;
                }

                return succDelete ? 0 : -99;
            }
            else
            {
                IsBossChanged = false;
                return -1;
            }
        }

        public int ShowProgress()
        {
            //TODO: 读取JSON中当前boss代号和血量
            return -1;
        }

        /// <summary>	
        /// 获取当前公会所在boss的代号
        /// <param name="status">当前会战进度</param>
        /// </summary>	
        public string GetCurrentBossID(GuildBattleStatus status)	
        {	
            const string BOSS_NUM = "abcde";
            return $"{status.Round}{BOSS_NUM[status.Order]}";	
        }

        /// <summary>
        /// 获取下一个周目的boss对应阶段
        /// </summary>
        /// /// <param name="status">当前会战进度</param>
        /// <returns>下一周目boss的阶段值</returns>
        public int GetNextRoundPhase(GuildBattleStatus status)
        {
            using SqlSugarClient dbClient = SugarUtils.CreateSqlSugarClient(DBPath);	
            //当前所处区服
            Server server =
                dbClient.Queryable<GuildData>()
                        .Where(guild => guild.Gid == GroupId)
                        .Select(guild => guild.ServerArea)
                        .First();
            //boss的最大阶段
            int maxPhase =
                dbClient.Queryable<GuildBattleBoss>()
                        .Where(boss => boss.Round == -1)
                        .Select(boss => boss.Phase)
                        .First();
            //已到最后一个阶段
            if (status.BossPhase == maxPhase) return maxPhase;
            //未达到最后一个阶段
            int nextRound = status.Round + 1;
            int nextPhase = status.BossPhase;
            //获取除了最后一阶段的所有round值，在获取到相应阶段后终止循环
            for (int i = 1; i < maxPhase; i++)
            {
                nextRound -= dbClient.Queryable<GuildBattleBoss>()
                                     .Where(boss => boss.Phase == i && boss.ServerId == server)
                                     .Select(boss => boss.Round)
                                     .First();
                if (nextRound <= 0) //得到下一个周目的阶段终止循环
                {
                    nextPhase = i;
                    break;
                }
            }
            if (nextRound > 0) nextPhase = maxPhase;
            return nextPhase;
        }
    }
}