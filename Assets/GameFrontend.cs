using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Backend;
using UnityEngine;

#nullable enable

public class GameFrontend : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        // This constructor arbitrarily assigns the local port number.
        UdpClient udpClient = new UdpClient(11000);
        try
        {
            udpClient.Connect("127.0.0.1",34567);

            // Sends a message to the host to which you have connected.
            

            var msg = new Message();
            msg.msg = "Hello";
            var b = JsonUtility.ToJson(msg);
            Byte[] sendBytes = Encoding.ASCII.GetBytes(b);

            udpClient.Send(sendBytes, sendBytes.Length);
            udpClient.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}


public class BuiltinUtils
{
    public static byte[] JsonSerialize<T>(T obj)
    {
        return Encoding.ASCII.GetBytes(JsonUtility.ToJson(obj));
    }
    public static T? JsonDeserialize<T>(byte[] bytes)
    {
        var s = Encoding.ASCII.GetString(bytes);
        return JsonUtility.FromJson<T>(s);
    }

    public static int? SerializeJsonInto<T>(T obj, byte[] bytes)
    {
        var b = BuiltinUtils.JsonSerialize(obj);
        if (b.Length > bytes.Length)
        {
            return null;
        }
        b.ToArray().CopyTo(bytes, 0);
        return b.Length;
    }

    public static int? findStructJsonBounds(byte[] bytes)
    {
        System.Collections.IEnumerator iter = bytes.GetEnumerator();
        if (!iter.MoveNext())
        {
            return null;
        }
        if ((byte)iter.Current != '{')
        {
            return null;
        }
        var count = 1;
        var indent = 1;
        while (iter.MoveNext())
        {
            count += 1;
            // TODO/FIXME: Need to support detecting if inside a string.
            if ((byte)iter.Current == '{')
            {
                indent += 1;
            }
            if ((byte)iter.Current == '}')
            {
                indent -= 1;
                if (indent == 0)
                {
                    return count;
                }
            }
        }
        return null;
    }


}
[Serializable]
public class RpcHeader : IMessage<RpcHeader>
{
    public string rpc { get; set; }
    public UInt32 body_size { get; set; }
    public bool is_return { get; set; }
}
public interface IMessage<CRTP_Self> where CRTP_Self : IMessage<CRTP_Self>
{
    static CRTP_Self? tryDeserialize(byte[] bytes)
    {

        return BuiltinUtils.JsonDeserialize<CRTP_Self>(bytes);
    }

    int? serializeInto(byte[] bytes)
    {
        return BuiltinUtils.SerializeJsonInto(this, bytes);
    }
}
public interface IChannel {
    void send(byte[] bytes);
    byte[] recv();
}


public class UDPChannel : IChannel
{
    private UdpClient client;
    private IPEndPoint peer;

    public UDPChannel(UdpClient client) {
        this.client = client;
    }
    public UDPChannel(UdpClient client, IPEndPoint peer)
    {
        this.client = client;
        this.peer = peer;
    }
    public byte[] recv()
    {
        IPEndPoint? endpoint = null;
        var ret = this.client.Receive(ref endpoint);
        if (this.peer == null) {
                this.peer = endpoint;
        }
        if (this.peer != endpoint && endpoint != null) {
            return ret;
        }
        // TODO(error_handling)
        throw new Exception("Unexpected peer.");
    }

    public void send(byte[] bytes)
    {
        client.Send(bytes, bytes.Length, peer);
    }
}


/// For example:
/// message ExampleMessage {
///     msg: string,
/// }
/// message ExampleReturn {
///     msg: string,
/// }
/// service Backend {
///     rpc ExampleMessage(ExampleMessage)
/// }

[Serializable]
public class ExampleMessage : IMessage<ExampleMessage>
{
    public String msg { get; set; }
}

[Serializable]
public class ExampleReturn : IMessage<ExampleReturn>
{
    public String msg { get; set; }
}

namespace Backend {

public interface RpcArgVariant {}
public interface RpcRetVariant {}

public class ExampleRpcArg : RpcArgVariant {
    public ExampleMessage Data {get; set;}
    public ExampleRpcArg(ExampleMessage data)
    {
        Data = data;
}
}
public class ExampleRpcRet : RpcRetVariant {
    public ExampleReturn Data { get; set;}
        public ExampleRpcRet(ExampleReturn data) {
            Data = data;
        }
}

public class Serializer {
        const string EXAMPLE_RPC_ID = "ExampleRpc";

        public static (RpcArgVariant?, int) ParseRpcRecv(byte[] bytes) {
            (RpcArgVariant?, int) fail = (null, 0);

            // Parse header
            var bound = BuiltinUtils.findStructJsonBounds(bytes);
            if (bound == null)
            {
                return fail;
            }
            var header = IMessage<RpcHeader>.tryDeserialize(bytes[..bound.Value]);
            if (header == null)
                return fail;

            // Parse body
            var start = bound.Value;
            var end = start + (int)header.body_size;
            switch (header.rpc) {
                case EXAMPLE_RPC_ID:
                    var ret = IMessage<ExampleMessage>.tryDeserialize(bytes[start..end]);
                    if (ret == null)
                        return fail;
                    return (new ExampleRpcArg(ret), end);
                default:
                    return fail;
            }
    }
        public static (RpcRetVariant?, int) ParseRpcResult(byte[] bytes)
        {
            (RpcRetVariant?, int) fail = (null, 0);

            // Parse header
            var bound = BuiltinUtils.findStructJsonBounds(bytes);
            if (bound == null)
            {
                return fail;
            }
            var header = IMessage<RpcHeader>.tryDeserialize(bytes[..bound.Value]);
            if (header == null)
                return fail;

            // Parse body
            var start = bound.Value;
            var end = start + (int)header.body_size;
            switch (header.rpc)
            {
                case EXAMPLE_RPC_ID:
                    var ret = IMessage<ExampleReturn>.tryDeserialize(bytes[start..end]);
                    if (ret == null)
                        return fail;
                    return (new ExampleRpcRet(ret), end);
                default:
                    throw new Exception("Unhandle case");
            }
        }

        public static byte[] SerializeRpcArg(RpcArgVariant arg) {
            // Create header
            var header = new RpcHeader();
            header.is_return = false;
            // Create body
            byte[] body;
            switch (arg) {
                case ExampleRpcArg targ:
                    body = BuiltinUtils.JsonSerialize(targ.Data);
                    header.rpc = EXAMPLE_RPC_ID;
                    break;
                default:
                    throw new Exception("Unhandle case");
            }
            // Finalize Header
            header.body_size = (UInt32)body.Length;
            var serialHeader = BuiltinUtils.JsonSerialize(header);
            // Stuff together into bytes
            byte[] ret = new byte[serialHeader.Length + body.Length];
            System.Array.Copy(serialHeader, ret, serialHeader.Length);
            System.Array.Copy(body, ret[serialHeader.Length..], body.Length);
            return ret;
        }

        public static byte[] SerializeRpcRet(RpcRetVariant arg)
        {
            // Create header
            var header = new RpcHeader();
            header.is_return = false;
            // Create body
            byte[] body;
            switch (arg)
            {
                case ExampleRpcRet targ:
                    body = BuiltinUtils.JsonSerialize(targ.Data);
                    header.rpc = EXAMPLE_RPC_ID;
                    break;
                default:
                    throw new Exception("Unhandle case");
            }
            // Finalize Header
            header.body_size = (UInt32)body.Length;
            var serialHeader = BuiltinUtils.JsonSerialize(header);
            // Stuff together into bytes
            byte[] ret = new byte[serialHeader.Length + body.Length];
            System.Array.Copy(serialHeader, ret, serialHeader.Length);
            System.Array.Copy(body, ret[serialHeader.Length..], body.Length);
            return ret;
        }
    }



    // User Interfaces
    public interface RpcHandler
    {
        ExampleReturn HandleExampleMessage(ExampleMessage msg);
        RpcRetVariant HandleRpcReceived(RpcArgVariant arg)
        {
            switch (arg)
            {
                case ExampleRpcArg arg1:
                    var res = HandleExampleMessage(arg1.Data);
                    return new ExampleRpcRet(res);
                default:
                    // All variants should be supported through autogeneration
                    throw new Exception("Unreachable case??");
            }
        }
    }

    public interface Service {
        void HandleLoop(IChannel chan, RpcHandler handler) {
            while (true)
            {
                byte[] bytes = chan.recv();
                var start = 0;
                while (true)
                {
                    // Parse the RPC
                    (RpcArgVariant? arg, int amt)  = Serializer.ParseRpcRecv(bytes[start..]);
                    if (arg == null) break;
                    // Handle the RPC
                    var res = handler.HandleRpcReceived(arg);
                    // Push the result
                    chan.send(Serializer.SerializeRpcRet(res));

                }
            }
        }
    }

    public class ServiceClient {
        private IChannel chan;
        ServiceClient(IChannel chan) {
            this.chan = chan;
        }

        RpcRetVariant call(RpcArgVariant arg) {
            this.chan.send(Serializer.SerializeRpcArg(arg));
            var bytes = this.chan.recv();
            var (res, amt) = Serializer.ParseRpcResult(bytes);
            // TODO(error_hadnling)
            if (res == null) throw new Exception("No result recvd");
            return res;
        }

        ExampleReturn call_example_message(ExampleMessage arg) {
            var res = (ExampleRpcRet)this.call(new ExampleRpcArg(arg));
            return res.Data;
        }
    }
}

