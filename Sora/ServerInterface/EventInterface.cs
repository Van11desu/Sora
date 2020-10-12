using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sora.Enumeration.ApiEnum;
using Sora.EventArgs.OnebotEvent.MessageEvent;
using Sora.EventArgs.OnebotEvent.MetaEvent;
using Sora.EventArgs.OnebotEvent.NoticeEvent;
using Sora.EventArgs.OnebotEvent.RequestEvent;
using Sora.EventArgs.SoraEvent;
using Sora.Tool;

namespace Sora.ServerInterface
{
    /// <summary>
    /// Onebot事件接口
    /// 判断和分发基类事件
    /// </summary>
    public class EventInterface
    {
        #region 静态记录表
        /// <summary>
        /// 心跳包记录
        /// </summary>
        internal static readonly Dictionary<Guid,long> HeartBeatList = new Dictionary<Guid, long>();
        #endregion

        #region 事件委托

        /// <summary>
        /// Onebot事件回调
        /// </summary>
        /// <typeparam name="TEventArgs">事件参数</typeparam>
        /// <param name="sender">产生事件的客户端</param>
        /// <param name="eventArgs">事件参数</param>
        /// <returns></returns>
        public delegate ValueTask EventAsyncCallBackHandler<in TEventArgs>(object sender, TEventArgs eventArgs)
            where TEventArgs : System.EventArgs;
        #endregion

        #region 事件回调
        /// <summary>
        /// 客户端链接完成事件
        /// </summary>
        public event EventAsyncCallBackHandler<ConnectEventArgs> OnClientConnect;
        /// <summary>
        /// 群聊事件
        /// </summary>
        public event EventAsyncCallBackHandler<GroupMessageEventArgs> OnGroupMessage;
        /// <summary>
        /// 私聊事件
        /// </summary>
        public event EventAsyncCallBackHandler<PrivateMessageEventArgs> OnPrivateMessage;
        /// <summary>
        /// 群申请事件
        /// </summary>
        public event EventAsyncCallBackHandler<GroupRequestEventArgs> OnGroupRequest;
        /// <summary>
        /// 好友申请事件
        /// </summary>
        public event EventAsyncCallBackHandler<FriendRequestEventArgs> OnFriendRequest;
        /// <summary>
        /// 群文件上传事件
        /// </summary>
        public event EventAsyncCallBackHandler<FileUploadEventArgs> OnFileUpload;
        /// <summary>
        /// 管理员变动事件
        /// </summary>
        public event EventAsyncCallBackHandler<AdminChangeEventArgs> OnAdminChange;
        /// <summary>
        /// 群成员变动事件
        /// </summary>
        public event EventAsyncCallBackHandler<GroupMemberChangeEventArgs> OnGroupMemberChange;
        /// <summary>
        /// 群成员禁言事件
        /// </summary>
        public event EventAsyncCallBackHandler<GroupMuteEventArgs> OnGroupMemberMute;
        #endregion

        #region 事件分发
        /// <summary>
        /// 事件分发
        /// </summary>
        /// <param name="messageJson">消息json对象</param>
        /// <param name="connection">客户端链接接口</param>
        internal void Adapter(JObject messageJson, Guid connection)
        {
            switch (GetBaseEventType(messageJson))
            {
                //元事件类型
                case "meta_event":
                    MetaAdapter(messageJson, connection);
                    break;
                case "message":
                    MessageAdapter(messageJson, connection);
                    break;
                case "request":
                    RequestAdapter(messageJson, connection);
                    break;
                case "notice":
                    NoticeAdapter(messageJson, connection);
                    break;
                default:
                    //尝试从响应中获取标识符
                    if (messageJson.TryGetValue("echo", out JToken echoJson) &&
                        Guid.TryParse(echoJson.ToString(), out Guid echo)    &&
                        //查找请求标识符是否存在
                        ApiInterface.RequestList.Any(e => e.Equals(echo)))
                    {
                        //取出返回值中的数据
                        ApiInterface.GetResponse(echo, messageJson);
                        break;
                    }
                    ConsoleLog.Debug("Sora",$"Unknown message :\r{messageJson}");
                    break;
            }
        }
        #endregion

        #region 元事件处理和分发
        /// <summary>
        /// 元事件处理和分发
        /// </summary>
        /// <param name="messageJson">消息</param>
        /// <param name="connection">连接GUID</param>
        private async void MetaAdapter(JObject messageJson, Guid connection)
        {
            switch (GetMetaEventType(messageJson))
            {
                //心跳包
                case "heartbeat":
                    HeartBeatEventArgs heartBeat = messageJson.ToObject<HeartBeatEventArgs>();
                    //TODO 暂时禁用心跳Log
                    //ConsoleLog.Debug("Sora",$"Get hreatbeat from [{connection}]");
                    if (heartBeat != null)
                    {
                        //刷新心跳包记录
                        if (HeartBeatList.Any(conn => conn.Key == connection))
                        {
                            HeartBeatList[connection] = heartBeat.Time;
                        }
                        else
                        {
                            HeartBeatList.TryAdd(connection,heartBeat.Time);
                        }
                    }
                    break;
                //生命周期
                case "lifecycle":
                    LifeCycleEventArgs lifeCycle = messageJson.ToObject<LifeCycleEventArgs>();
                    if (lifeCycle != null) ConsoleLog.Debug("Sore", $"Lifecycle event[{lifeCycle.SubType}] from [{connection}]");
                    //未知原因会丢失第一次调用的返回值，直接丢弃第一次调用
                    await ApiInterface.GetClientInfo(connection);
                    (int retCode, ClientType clientType, string clientVer) = await ApiInterface.GetClientInfo(connection);
                    if (retCode != 0)//检查返回值
                    {
                        SoraWSServer.ConnectionInfos[connection].Close();
                        ConsoleLog.Info("Sora",$"检查客户端版本时发生错误，已断开与客户端的连接(retcode={retCode})");
                        break;
                    }
                    ConsoleLog.Info("Sora",$"已连接到{Enum.GetName(clientType)}客户端,版本:{clientVer}");
                    if(OnClientConnect == null) break;
                    //执行回调函数
                    await OnClientConnect(typeof(EventInterface),
                                          new ConnectEventArgs(connection, "lifecycle",
                                                               lifeCycle?.SelfID ?? -1, clientType, clientVer,
                                                               lifeCycle?.Time   ?? 0));
                    break;
                default:
                    ConsoleLog.Warning("Sora",$"接收到未知事件[{GetMetaEventType(messageJson)}]");
                    break;
            }
        }
        #endregion

        #region 消息事件处理和分发
        /// <summary>
        /// 消息事件处理和分发
        /// </summary>
        /// <param name="messageJson">消息</param>
        /// <param name="connection">连接GUID</param>
        private async void MessageAdapter(JObject messageJson, Guid connection)
        {
            switch (GetMessageType(messageJson))
            {
                //私聊事件
                case "private":
                    ServerPrivateMsgEventArgs privateMsg = messageJson.ToObject<ServerPrivateMsgEventArgs>();
                    if(privateMsg == null) break;
                    ConsoleLog.Debug("Sora",$"Private msg {privateMsg.SenderInfo.Nick}({privateMsg.UserId}) : {privateMsg.RawMessage}");
                    //执行回调函数
                    if(OnPrivateMessage == null) break;
                    await OnPrivateMessage(typeof(EventInterface),
                                           new PrivateMessageEventArgs(connection, "private", privateMsg));
                    break;
                //群聊事件
                case "group":
                    ServerGroupMsgEventArgs groupMsg = messageJson.ToObject<ServerGroupMsgEventArgs>();
                    if(groupMsg == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group msg[{groupMsg.GroupId}] form {groupMsg.SenderInfo.Nick}[{groupMsg.UserId}] : {groupMsg.RawMessage}");
                    //执行回调函数
                    ConsoleLog.Debug("Sora",$"Thread id{Thread.CurrentThread.ManagedThreadId}");
                    if(OnGroupMessage == null) break;
                    await OnGroupMessage(typeof(EventInterface),
                                         new GroupMessageEventArgs(connection, "group", groupMsg));
                    break;
                default:
                    ConsoleLog.Warning("Sora",$"接收到未知事件[{GetMessageType(messageJson)}]");
                    break;
            }
        }
        #endregion

        #region 请求事件处理和分发
        /// <summary>
        /// 请求事件处理和分发
        /// </summary>
        /// <param name="messageJson">消息</param>
        /// <param name="connection">连接GUID</param>
        private async void RequestAdapter(JObject messageJson, Guid connection)
        {
            switch (GetRequestType(messageJson))
            {
                //好友请求事件
                case "friend":
                    ServerFriendRequestEventArgs friendRequest = messageJson.ToObject<ServerFriendRequestEventArgs>();
                    if(friendRequest == null)  break;
                    ConsoleLog.Debug("Sora",$"Friend request form [{friendRequest.UserId}] with commont[{friendRequest.Comment}] | flag[{friendRequest.Flag}]");
                    //执行回调函数
                    if(OnFriendRequest == null) break;
                    await OnFriendRequest(typeof(EventInterface),
                                          new FriendRequestEventArgs(connection, "request|friend",
                                                                     friendRequest));
                    break;
                //群组请求事件
                case "group":
                    if (messageJson.TryGetValue("sub_type",out JToken sub) && sub.ToString().Equals("notice"))
                    {
                        ConsoleLog.Warning("Sora","收到notice消息类型，不解析此类型消息");
                        break;
                    }
                    ServerGroupRequestEventArgs groupRequest = messageJson.ToObject<ServerGroupRequestEventArgs>();
                    if(groupRequest == null) break;
                    ConsoleLog.Debug("Sora",$"Group request [{groupRequest.GroupRequestType}] form [{groupRequest.UserId}] with commont[{groupRequest.Comment}] | flag[{groupRequest.Flag}]");
                    //执行回调函数
                    if(OnGroupRequest == null) break;
                    await OnGroupRequest(typeof(EventInterface),
                                         new GroupRequestEventArgs(connection, "request|group",
                                                                   groupRequest));
                    break;
                default:
                    ConsoleLog.Warning("Sora",$"接收到未知事件[{GetRequestType(messageJson)}]");
                    break;
            }
        }
        #endregion

        #region 通知事件处理和分发
        /// <summary>
        /// 通知事件处理和分发
        /// </summary>
        /// <param name="messageJson">消息</param>
        /// <param name="connection">连接GUID</param>
        private async void NoticeAdapter(JObject messageJson, Guid connection)
        {
            switch (GetNoticeType(messageJson))
            {
                //群文件上传
                case "group_upload":
                    ServerFileUploadEventArgs fileUpload = messageJson.ToObject<ServerFileUploadEventArgs>();
                    if(fileUpload == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group notice[Upload file] file[{fileUpload.Upload.Name}] from group[{fileUpload.GroupId}({fileUpload.UserId})]");
                    //执行回调函数
                    if(OnFileUpload == null) break;
                    await OnFileUpload(typeof(EventInterface),
                                       new FileUploadEventArgs(connection, "group_upload", fileUpload));
                    break;
                //群管理员变动
                case "group_admin":
                    ServerAdminChangeEventArgs adminChange = messageJson.ToObject<ServerAdminChangeEventArgs>();
                    if(adminChange == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group amdin change[{adminChange.SubType}] from group[{adminChange.GroupId}] by[{adminChange.UserId}]");
                    //执行回调函数
                    if(OnAdminChange == null) break;
                    await OnAdminChange(typeof(EventInterface),
                                        new AdminChangeEventArgs(connection, "group_upload", adminChange));
                    break;
                //群成员变动
                case "group_decrease":case "group_increase":
                    ServerGroupMemberChangeEventArgs groupMemberChange = messageJson.ToObject<ServerGroupMemberChangeEventArgs>();
                    if (groupMemberChange == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"{groupMemberChange.NoticeType} type[{groupMemberChange.SubType}] member {groupMemberChange.GroupId}[{groupMemberChange.UserId}]");
                    //执行回调函数
                    if(OnGroupMemberChange == null) break;
                    await OnGroupMemberChange(typeof(EventInterface),
                                              new GroupMemberChangeEventArgs(connection, "group_upload", groupMemberChange));
                    break;
                //群禁言
                case "group_ban":
                    ServerGroupMuteEventArgs groupMute = messageJson.ToObject<ServerGroupMuteEventArgs>();
                    if (groupMute == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group[{groupMute.GroupId}] {groupMute.ActionType} member[{groupMute.UserId}]{groupMute.Duration}");
                    //执行回调函数
                    if(OnGroupMemberMute == null) break;
                    await OnGroupMemberMute(typeof(EventInterface),
                                            new GroupMuteEventArgs(connection, "group_upload", groupMute));
                    break;
                //好友添加
                case "friend_add":
                    FriendAddEventArgs friendAdd = messageJson.ToObject<FriendAddEventArgs>();
                    if(friendAdd == null) break;
                    ConsoleLog.Debug("Sora",$"Friend add user[{friendAdd.UserId}]");
                    break;
                //群消息撤回
                case "group_recall":
                    GroupRecallEventArgs groupRecall = messageJson.ToObject<GroupRecallEventArgs>();
                    if(groupRecall == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group[{groupRecall.GroupId}] recall by [{groupRecall.OperatorId}],msg id={groupRecall.MessageId} sender={groupRecall.UserId}");
                    break;
                //好友消息撤回
                case "friend_recall":
                    FriendRecallEventArgs friendRecall = messageJson.ToObject<FriendRecallEventArgs>();
                    if(friendRecall == null) break;
                    ConsoleLog.Debug("Sora", $"Friend[{friendRecall.UserId}] recall msg id={friendRecall.MessageId}");
                    break;
                //群名片变更
                //此事件仅在Go上存在
                case "group_card":
                    GroupCardUpdateEventArgs groupCardUpdate = messageJson.ToObject<GroupCardUpdateEventArgs>();
                    if(groupCardUpdate == null) break;
                    ConsoleLog.Debug("Sora",
                                     $"Group[{groupCardUpdate.GroupId}] member[{groupCardUpdate.UserId}] card update [{groupCardUpdate.OldCard} => {groupCardUpdate.NewCard}]");
                    break;
                //通知类事件
                case "notify":
                    switch (GetNotifyType(messageJson))
                    {
                        case "poke"://戳一戳
                            PokeOrLuckyEventArgs pokeEvent = messageJson.ToObject<PokeOrLuckyEventArgs>();
                            if(pokeEvent == null) break;
                            ConsoleLog.Debug("Sora",
                                             $"Group[{pokeEvent.GroupId}] poke from [{pokeEvent.UserId}] to [{pokeEvent.TargetId}]");
                            break;
                        case "lucky_king"://运气王
                            PokeOrLuckyEventArgs luckyEvent = messageJson.ToObject<PokeOrLuckyEventArgs>();
                            if(luckyEvent == null) break;
                            ConsoleLog.Debug("Sora",
                                             $"Group[{luckyEvent.GroupId}] lucky king user[{luckyEvent.TargetId}]");
                            break;
                        case "honor":
                            HonorEventArgs honorEvent = messageJson.ToObject<HonorEventArgs>();
                            if (honorEvent == null) break;
                            ConsoleLog.Debug("Sora",
                                             $"Group[{honorEvent.GroupId}] member honor change [{honorEvent.HonorType}]");
                            break;
                        default:
                            ConsoleLog.Warning("Sora",$"未知Notify事件类型[{GetNotifyType(messageJson)}]");
                            break;
                    }
                    break;
                default:
                    ConsoleLog.Debug("Sora",$"unknown notice \n{messageJson}");
                    ConsoleLog.Warning("Sora",$"接收到未知事件[{GetNoticeType(messageJson)}]");
                    break;
            }
        }
        #endregion

        #region 事件类型获取
        /// <summary>
        /// 获取上报事件类型
        /// </summary>
        /// <param name="messageJson">消息Json对象</param>
        private static string GetBaseEventType(JObject messageJson) =>
            !messageJson.TryGetValue("post_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        /// <summary>
        /// 获取元事件类型
        /// </summary>
        /// <param name="messageJson">消息Json对象</param>
        private static string GetMetaEventType(JObject messageJson) =>
            !messageJson.TryGetValue("meta_event_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        /// <summary>
        /// 获取消息事件类型
        /// </summary>
        /// <param name="messageJson">消息Json对象</param>
        private static string GetMessageType(JObject messageJson) =>
            !messageJson.TryGetValue("message_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        /// <summary>
        /// 获取请求事件类型
        /// </summary>
        /// <param name="messageJson">消息Json对象</param>
        private static string GetRequestType(JObject messageJson) =>
            !messageJson.TryGetValue("request_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        /// <summary>
        /// 获取通知事件类型
        /// </summary>
        /// <param name="messageJson">消息Json对象</param>
        private static string GetNoticeType(JObject messageJson) =>
            !messageJson.TryGetValue("notice_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        /// <summary>
        /// 获取通知事件子类型
        /// </summary>
        /// <param name="messageJson"></param>
        private static string GetNotifyType(JObject messageJson) =>
            !messageJson.TryGetValue("sub_type", out JToken typeJson) ? string.Empty : typeJson.ToString();

        #endregion
    }
}
