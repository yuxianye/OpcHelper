﻿using Opc;
using Opc.Da;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpcHelper
{
    /// <summary>
    /// OPC客户端辅助类
    /// </summary>
    public class OpcClientHelper : IDisposable
    {
        public OpcClientHelper()
        {
            initdaemonTimer();
        }

        /// <summary>
        /// 已连接的服务器
        /// </summary>
        private Opc.Da.Server opcServer = null;

        /// <summary>
        /// 守护Timer
        /// </summary>
        private System.Timers.Timer daemonTimer = new System.Timers.Timer();

        /// <summary>
        /// 数据改变事件，此事件只能在连接之后注册，断开连接之前取消注册
        /// </summary>
        public event EventHandler<OpcDataEventArgs> OnDataChanged;

        /// <summary>
        /// 异常事件
        /// </summary>
        public event EventHandler<OpcErrorEventArgs> OnErrorHappened;

        /// <summary>
        /// 日志消息事件
        /// </summary>
        public event EventHandler<OpcLogEventArgs> OnLogHappened;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// Opc服务器名称
        /// </summary>
        private string serverName;

        /// <summary>
        /// 主机名
        /// </summary>
        private string host;

        /// <summary>
        /// 初始化守护timer
        /// </summary>
        private void initdaemonTimer()
        {
            daemonTimer.Interval = 5 * 1000;
            daemonTimer.Elapsed += DaemonTimer_Elapsed;
            daemonTimer.AutoReset = true;
        }

        /// <summary>
        /// 守护Timer回调函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DaemonTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (IsConnected)
            {
                reRegisterOpcDataItems();
            }
            else
            {
                reCconnect();
                reRegisterOpcDataItems();
            }
        }

        /// <summary>
        /// 取得可用的Opc服务器
        /// </summary>
        /// <param name="host">Opc服务器主机名称或者IP,默认值为本机</param>
        /// <returns>可用的服务器名称</returns>
        public static IEnumerable<string> GetOpcServers(string host = "127.0.0.1")
        {
            try
            {
                var result = getOpcServers(host);
                return result == null ? null : result.Select(a => a.Name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Print(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 取得可用的Opc服务器
        /// </summary>
        /// <param name="host">Opc服务器主机名称或者IP,默认值为本机</param>
        /// <returns>可用的服务器</returns>
        private static Opc.Server[] getOpcServers(string host = "127.0.0.1")
        {
            try
            {
                Opc.IDiscovery opcdiscovery = new OpcCom.ServerEnumerator();
                Opc.Server[] servers = opcdiscovery.GetAvailableServers(Specification.COM_DA_20, host, null);
                return servers;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Print(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="serverName"></param>
        /// <param name="host"></param>
        public OpcResult Connect(string serverName, string host = "127.0.0.1")
        {
            OpcResult opcResult = OpcResult.Unknow;
            try
            {
                //daemonTimer.Stop();

                this.serverName = serverName;
                this.host = host;
                if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(host))
                {
                    if (!Equals(null, OnLogHappened))
                    {
                        OnLogHappened(this, new OpcLogEventArgs("未指定服务器或主机名称"));
                    }
                    opcResult = OpcResult.E_FAIL;
                    return opcResult;
                }
                if (IsConnected)
                {
                    if (!Equals(null, OnLogHappened))
                    {
                        OnLogHappened(this, new OpcLogEventArgs("Opc服务器已经连接,host=" + host + ",serverName=" + serverName));
                    }
                    opcResult = OpcResult.S_OK;
                    return opcResult;
                }
                var servers = OpcClientHelper.getOpcServers(host);
                if (!Equals(null, servers) && servers.Any(a => a.Name == serverName))
                {
                    opcServer = (Opc.Da.Server)servers.FirstOrDefault(a => a.Name == serverName);
                    opcServer.Connect();
                    //服务器断开事件
                    opcServer.ServerShutdown += opcServer_ServerShutdown;
                    IsConnected = true;
                    if (!Equals(null, OnLogHappened))
                    {
                        OnLogHappened(this, new OpcLogEventArgs("连接Opc服务器成功,host=" + host + ",serverName=" + serverName));
                    }
                }
                daemonTimer.Start();
                opcResult = OpcResult.S_OK;
                return opcResult;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                //System.Diagnostics.Debug.Print(ex.Message);
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "连接Opc服务器时出错：" + ex.Message, ex));
                }
                opcResult = OpcResult.E_FAIL;
                return opcResult;
            }
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        private void reCconnect()
        {
            if (!Equals(null, OnLogHappened))
            {
                OnLogHappened(this, new OpcHelper.OpcLogEventArgs("开始重新连接Opc服务器"));
            }
            Connect(serverName, host);
            //daemonTimer.Start();
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public OpcResult DisConnect()
        {
            //daemonTimer.Stop();
            daemonTimer.Enabled = false;
            return disConnect();

        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <returns></returns>
        private OpcResult disConnect()
        {
            OpcResult opcResult = OpcResult.E_FAIL;
            try
            {
                if (!IsConnected)
                {
                    if (!Equals(null, OnLogHappened))
                    {
                        OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                    }
                    opcResult = OpcResult.ServerNoConnect;
                    return opcResult;
                }
                var subscriptionCount = opcServer.Subscriptions.Count;
                for (int i = subscriptionCount - 1; i >= 0; i--)
                {
                    var subscription = (opcServer.Subscriptions[i] as Opc.Da.Subscription);
                    subscription.DataChanged -= new Opc.Da.DataChangedEventHandler(ThisSubscription_DataChanged);
                    subscription.RemoveItems(subscription.Items);
                    this.opcServer.CancelSubscription(subscription);
                    //opcServer.Subscriptions[i].Dispose();
                }
                this.opcServer.Disconnect();
                if (!Equals(null, OnLogHappened))
                {
                    OnLogHappened(this, new OpcLogEventArgs("断开Opc服务器成功"));
                }
                opcResult = OpcResult.S_OK;
                return opcResult;
            }
            catch (Exception ex)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "断开Opc服务器时出错，" + ex.Message, ex));
                }
                opcResult = OpcResult.E_FAIL;
                return opcResult;
            }
            finally
            {
                IsConnected = false;
            }
        }

        /// <summary>
        /// Opc数据项集合
        /// </summary>
        public IList<OpcDataItem> OpcDataItems { get; private set; }

        /// <summary>
        /// 注册Opc数据项集合
        /// </summary>
        /// <param name="opcDataItems"></param>
        public void RegisterOpcDataItems(IList<OpcDataItem> opcDataItems)
        {
            this.OpcDataItems = opcDataItems;
            //daemonTimer.Stop();

            //如果未连接那么返回
            if (!this.IsConnected)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "Opc服务器未连接，请先连接在订阅数据项", null));
                }
                return;
            }
            #region 没有数据项那么清空所有订阅

            //如果订阅的DataItem没有，那么清空现在所有订阅
            if (Equals(null, opcDataItems) || opcDataItems.Count() < 1)
            {
                if (!Equals(this.opcServer.Subscriptions, null) && this.opcServer.Subscriptions.Count > 0)
                {
                    foreach (Opc.Da.Subscription thisSubscription in this.opcServer.Subscriptions)
                    {
                        thisSubscription.RemoveItems(thisSubscription.Items);
                        thisSubscription.DataChanged -= ThisSubscription_DataChanged;
                        this.opcServer.CancelSubscription(thisSubscription);
                        thisSubscription.Dispose();
                    }
                    this.opcServer.Subscriptions.Clear();

                    //订阅项取消后通知外部程序
                    if (!Equals(null, OnLogHappened))
                    {
                        OnLogHappened(this, new OpcHelper.OpcLogEventArgs("已经取消所有订阅"));
                    }
                    return;
                }
                //return true;
            }

            #endregion  有数据项需要更新或者删除

            //根据要订阅的新数据项，增加或者移除现在的订阅
            if (!IsConnected)
            {
                if (!Equals(null, OnLogHappened))
                {
                    OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                }
                return;
            }
            //根据更新频率分组
            var dataItemGroups = from a in opcDataItems
                                 group a by a.UpdateRate into g
                                 select new { UpdateRate = g.Key, DataItems = g };

            //循环每组订阅对象，查询是否实际订阅，有则更新，没有则新建
            foreach (var dataItemGroup in dataItemGroups)
            {
                Opc.Da.Subscription thisSubscription = this.opcServer.Subscriptions.Cast<Opc.Da.Subscription>().FirstOrDefault(a => a.State.UpdateRate == dataItemGroup.UpdateRate);
                //未找到已经订阅的组，那么新建组并添加订阅项
                if (Equals(null, thisSubscription))
                {
                    if (!IsConnected)
                    {
                        if (!Equals(null, OnLogHappened))
                        {
                            OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                        }
                        return;
                    }
                    #region 增加订阅

                    //创建订阅组。订阅组状态的名称是更新频率
                    Opc.Da.Subscription newSubscription = this.opcServer.CreateSubscription
                        (CreateSubscriptionState(dataItemGroup.UpdateRate.ToString(), dataItemGroup.UpdateRate))
                        as Opc.Da.Subscription;//创建组
                    //创建opc订阅项
                    var opcItems = ConvertItems(dataItemGroup.DataItems);
                    //添加订阅项
                    var results = newSubscription.AddItems(opcItems.ToArray());
                    newSubscription.DataChanged += new Opc.Da.DataChangedEventHandler(ThisSubscription_DataChanged);
                    //订阅的结果通过事件通知给外部调用程序
                    foreach (var v in results)
                    {
                        if (!IsConnected)
                        {
                            if (!Equals(null, OnLogHappened))
                            {
                                OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                            }
                            return;
                        }
                        //未订阅成功，异常事件通知
                        if (v.ResultID == Opc.ResultID.S_OK && !Equals(null, OnDataChanged))
                        {
                            OnDataChanged(this, new OpcDataEventArgs(OpcResult.DataItemRegistered,
                                dataItemGroup.DataItems.FirstOrDefault(a => a.Name == v.ItemName)));
                        }
                        else if (v.ResultID != Opc.ResultID.S_OK && !Equals(null, OnErrorHappened))
                        {
                            OpcResult opcResult = OpcResult.Unknow;
                            Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                            this.OpcDataItems.FirstOrDefault(a => a.Name == v.ItemName).Quality = opcResult;
                            OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "订阅数据项时发生错误" + v.ResultID.Name, null));
                        }
                    }//end foreach
                    #endregion
                }
                else//已经有订阅组，那么更新订阅项
                {
                    if (!IsConnected)
                    {
                        if (!Equals(null, OnLogHappened))
                        {
                            OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                        }
                        return;
                    }
                    #region 已经有订阅组，那么更新订阅项

                    //查询要新增加的点，
                    IList<OpcDataItem> newItems = new List<OpcDataItem>();
                    foreach (var v in dataItemGroup.DataItems)
                    {
                        if (!thisSubscription.Items.Any(a => a.ItemName == v.Name))
                        {
                            newItems.Add(v);
                        }
                    }
                    var opcItems = ConvertItems(newItems);
                    //添加订阅项
                    var addResults = thisSubscription.AddItems(opcItems.ToArray());

                    //订阅的结果通过事件通知给外部调用程序
                    foreach (var v in addResults)
                    {
                        if (!IsConnected)
                        {
                            if (!Equals(null, OnLogHappened))
                            {
                                OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                            }
                            return;
                        }
                        //未订阅成功，异常事件通知
                        if (v.ResultID == Opc.ResultID.S_OK && !Equals(null, OnDataChanged))
                        {
                            OnDataChanged(this, new OpcDataEventArgs(OpcResult.DataItemRegistered,
                                dataItemGroup.DataItems.FirstOrDefault(a => a.Name == v.ItemName)));
                        }
                        else if (v.ResultID != Opc.ResultID.S_OK && !Equals(null, OnErrorHappened))
                        {
                            OpcResult opcResult = OpcResult.Unknow;
                            Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                            this.OpcDataItems.FirstOrDefault(a => a.Name == v.ItemName).Quality = opcResult;
                            OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "订阅数据项时发生错误:" + v.ItemName + " 无效,OpcResult=" + v.ResultID.Name, null));
                        }
                    }//end foreach

                    //查询要删除的点，
                    IList<Opc.Da.Item> deleteItems = new List<Opc.Da.Item>();
                    foreach (var v in thisSubscription.Items)
                    {
                        if (!dataItemGroup.DataItems.Any(a => a.Name == v.ItemName))
                        {
                            deleteItems.Add(v);
                        }
                    }

                    //删除已经订阅点
                    var deleteResults = thisSubscription.RemoveItems(deleteItems.ToArray());
                    //订阅的结果通过事件通知给外部调用程序
                    foreach (var v in deleteResults)
                    {
                        if (!IsConnected)
                        {
                            if (!Equals(null, OnLogHappened))
                            {
                                OnLogHappened(this, new OpcLogEventArgs("Opc服务器已断开"));
                            }
                            return;
                        }
                        //未订阅成功，异常事件通知
                        if (v.ResultID == Opc.ResultID.S_OK && !Equals(null, OnDataChanged))
                        {
                            OpcDataItem tmp2 = new OpcDataItem(v.ItemName, thisSubscription.State.UpdateRate, "", "", OpcResult.DataItemUnregistered);
                            OnDataChanged(this, new OpcDataEventArgs(OpcResult.DataItemUnregistered, tmp2));
                        }
                        else if (v.ResultID != Opc.ResultID.S_OK && !Equals(null, OnErrorHappened))
                        {
                            OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "取消订阅数据项时发生错误" + v.ResultID.Name, null));
                        }

                    }//end foreach
                    //删除订阅项后没有item了，那么删除thisSubscription
                    if (thisSubscription.Items.Count() < 1)
                    {
                        thisSubscription.DataChanged -= ThisSubscription_DataChanged;
                        string name = thisSubscription.Name;
                        //this.opcServer.Subscriptions.Remove(thisSubscription);
                        this.opcServer.CancelSubscription(thisSubscription);
                        thisSubscription.Dispose();//
                        if (!Equals(null, OnLogHappened))
                        {
                            OnLogHappened(this, new OpcLogEventArgs("Subscription " + name + " 已经从订阅组中移除"));
                        }
                        name = null;
                    }

                    #endregion
                }

            }//end foreach (var dataItemGroup in dataItemGroups)

            daemonTimer.Start();
        }

        /// <summary>
        /// 重新注册数据点
        /// </summary>
        private void reRegisterOpcDataItems()
        {
            if (IsConnected
               && !Equals(null, this.OpcDataItems)
                && OpcDataItems.Count() > 0
                && OpcDataItems.Count(a => a.Quality != OpcResult.S_OK) > 0)
            {
                if (!Equals(null, OnLogHappened))
                {
                    OnLogHappened(this, new OpcHelper.OpcLogEventArgs("开始重新注册数据点"));
                }
                RegisterOpcDataItems(OpcDataItems);
            }
        }
        /// <summary>
        /// 数据改变事件函数
        /// </summary>
        /// <param name="subscriptionHandle"></param>
        /// <param name="requestHandle"></param>
        /// <param name="values"></param>
        private void ThisSubscription_DataChanged(object subscriptionHandle, object requestHandle, Opc.Da.ItemValueResult[] values)
        {
            if (!Equals(null, OnDataChanged))
            {
                foreach (var v in values)
                {
                    OpcDataItem item = OpcDataItems.FirstOrDefault(a => a.Name == v.ItemName);
                    if (Equals(null, item))
                    {
                        //OnDataChanged(this, new OpcDataEventArgs(OpcResult.E_FAIL, item));
                        continue;
                    }
                    OpcResult opcResult = OpcResult.Unknow;
                    Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                    item.Name = v.ItemName;
                    item.Quality = opcResult;
                    item.OldValue = item.NewValue;
                    item.NewValue = v.Value.ToString();
                    OnDataChanged(this, new OpcDataEventArgs(opcResult, item));
                }
            }
        }

        /// <summary>
        /// 创建订阅状态对象
        /// </summary>
        /// <param name="name"></param>
        /// <param name="updateRate"></param>
        /// <returns></returns>
        private Opc.Da.SubscriptionState CreateSubscriptionState(string name, int updateRate)
        {
            //设定组状态
            Opc.Da.SubscriptionState subscriptionState = new Opc.Da.SubscriptionState();
            subscriptionState.Name = name;
            subscriptionState.ServerHandle = null;
            subscriptionState.ClientHandle = Guid.NewGuid().ToString();
            subscriptionState.Active = true;
            subscriptionState.UpdateRate = updateRate;
            subscriptionState.Deadband = 0;
            subscriptionState.Locale = null;
            return subscriptionState;
        }

        /// <summary>
        /// 将<seealso cref="OpcDataItem"/> 类型转换成<seealso cref="Opc.Da.Item"/>类型
        /// </summary>
        /// <param name="subscriptionItems"></param>
        /// <returns></returns>
        private IEnumerable<Opc.Da.Item> ConvertItems(IEnumerable<OpcDataItem> subscriptionItems)
        {
            var opcItems = from item in subscriptionItems
                           select new Opc.Da.Item
                           {
                               ItemName = item.Name,
                               ClientHandle = Guid.NewGuid().ToString(),
                               ItemPath = null,
                           };
            return opcItems;
        }

        /// <summary>
        /// 写数据
        /// </summary>
        /// <param name="opcDataItem"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public OpcResult Write(OpcDataItem opcDataItem, object value)
        {
            OpcResult opcResult = OpcResult.Unknow;
            //如果未连接那么返回
            if (!this.IsConnected)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "Opc服务器未连接，请先连接再写数据项", null));
                }
                return opcResult;
            }
            try
            {
                if (Equals(null, opcDataItem))
                {
                    throw new ArgumentNullException("opcDataItem参数不能为空。");
                }
                Opc.Da.Subscription tmpSubscription =
                    this.opcServer.Subscriptions.Cast<Opc.Da.Subscription>().FirstOrDefault(a => a.State.UpdateRate == opcDataItem.UpdateRate);
                var itemValue =
                      new ItemValue((ItemIdentifier)tmpSubscription.Items.FirstOrDefault(a => a.ItemName == opcDataItem.Name));
                itemValue.Value = opcDataItem.NewValue;
                var results = tmpSubscription.Write(new ItemValue[] { itemValue });

                for (int i = 0; i < 20; i++)
                {
                    results = tmpSubscription.Write(new ItemValue[] { itemValue });
                }
                //results = tmpSubscription.Write(new ItemValue[] { itemValue });
                //Thread.Sleep(opcDataItem.UpdateRate + 5);//暂停线程以让DataChange反映
                if (results.Count() < 1 && !Equals(null, OnErrorHappened))
                {
                    opcResult = OpcResult.E_UNKNOWN_ITEM_NAME;
                    OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "写入数据项时发生错误，未找到数据项:" + opcDataItem.Name, null));
                    opcDataItem.Quality = OpcResult.Unknow;
                    return opcResult;
                }
                foreach (var v in results)
                {
                    Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                    if (v.ResultID != Opc.ResultID.S_OK && !Equals(null, OnErrorHappened))
                    {
                        Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                        OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "写入数据项时发生错误:" + v.ResultID.Name, null));
                    }
                }//end foreach
                return opcResult;
            }
            catch (Exception ex)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcHelper.OpcErrorEventArgs(OpcResult.E_FAIL, "写入数据时错误。", ex));
                }
                return opcResult;
            }
        }

        /// <summary>
        /// 读数据
        /// </summary>
        /// <param name="opcDataItem"></param>
        /// <returns></returns>
        public OpcDataItem Read(OpcDataItem opcDataItem)
        {
            OpcDataItem opcDataItemResult = null;

            //如果未连接那么返回
            if (!this.IsConnected)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.E_FAIL, "Opc服务器未连接，请先连接再读取数据项", null));
                }
                return opcDataItemResult;
            }
            try
            {
                if (Equals(null, opcDataItem))
                {
                    throw new ArgumentNullException("opcDataItem参数不能为空。");
                }
                Opc.Da.Subscription tmpSubscription =
                    this.opcServer.Subscriptions.Cast<Opc.Da.Subscription>().FirstOrDefault(a => a.State.UpdateRate == opcDataItem.UpdateRate);
                var item = tmpSubscription.Items.Where(a => a.ItemName == opcDataItem.Name);
                var results = tmpSubscription.Read(item.ToArray());
                OpcResult opcResult;
                if (results.Count() < 1 && !Equals(null, OnErrorHappened))
                {
                    opcResult = OpcResult.E_UNKNOWN_ITEM_NAME;
                    OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "读取数据项时发生错误，未找到数据项:" + opcDataItem.Name, null));
                    opcDataItem.Quality = OpcResult.E_UNKNOWN_ITEM_NAME;
                    return opcDataItem;
                }
                foreach (var v in results)
                {
                    Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                    opcDataItemResult = this.OpcDataItems.FirstOrDefault(a => a.Name == opcDataItem.Name).Clone() as OpcDataItem;
                    opcDataItemResult.Quality = opcResult;
                    opcDataItemResult.OldValue = opcDataItemResult.NewValue;
                    opcDataItemResult.NewValue = v.Value;
                    if (v.ResultID != Opc.ResultID.S_OK && !Equals(null, OnErrorHappened))
                    {
                        Enum.TryParse<OpcResult>(v.ResultID.ToString(), out opcResult);
                        OnErrorHappened(this, new OpcErrorEventArgs(opcResult, "读取数据项时发生错误:" + v.ResultID.Name, null));
                    }
                }//end foreach
                return opcDataItemResult;
            }
            catch (Exception ex)
            {
                if (!Equals(null, OnErrorHappened))
                {
                    OnErrorHappened(this, new OpcHelper.OpcErrorEventArgs(OpcResult.E_FAIL, "读取数据时错误。", ex));
                }
                return opcDataItemResult;
            }
        }

        private bool isHasEvent(OpcClientHelper opcClientHelper, string eventName)
        {
            Assembly a = Assembly.GetAssembly(opcClientHelper.GetType());
            System.Type t = a.GetType(opcClientHelper.GetType().FullName, true);

            System.Reflection.MethodInfo[] methods = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (MethodInfo m in methods)
            {
                if (m.Name.Equals(eventName))
                {
                    return true;
                }
            }
            return false;
        }

        private bool isHasEvent2(Opc.Da.Subscription subscription, string eventName)
        {
            Assembly a = Assembly.GetAssembly(subscription.GetType());
            System.Type t = a.GetType(subscription.GetType().FullName, true);

            System.Reflection.MethodInfo[] methods = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (MethodInfo m in methods)
            {
                if (m.Name.Equals(eventName))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 服务器关闭回调函数
        /// </summary>
        /// <param name="reason"></param>
        private void opcServer_ServerShutdown(string reason)
        {
            if (!Equals(null, OnErrorHappened))
            {
                OnErrorHappened(this, new OpcErrorEventArgs(OpcResult.ServerShutdown, reason, null));
            }
            disConnect();
            daemonTimer.Start();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            this.OnDataChanged = null;
            DisConnect();
            this.OnErrorHappened = null;
            this.OnDataChanged = null;
            this.OpcDataItems = null;
            this.opcServer = null;
        }
    }
}
