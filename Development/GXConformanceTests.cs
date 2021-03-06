﻿//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
//
// Version:         $Revision: 9442 $,
//                  $Date: 2017-05-23 15:21:03 +0300 (ti, 23 touko 2017) $
//                  $Author: gurux01 $
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// More information of Gurux DLMS/COSEM Director: http://www.gurux.org/GXDLMSDirector
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.Common;
using Gurux.DLMS;
using Gurux.DLMS.Conformance.Test;
using Gurux.DLMS.Enums;
using Gurux.DLMS.ManufacturerSettings;
using Gurux.DLMS.Objects;
using Gurux.DLMS.Objects.Enums;
using Gurux.DLMS.UI;
using GXDLMS.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace GXDLMSDirector
{
    /// <summary>
    /// This class implements Gurux Conformance tests.
    /// </summary>
    public class GXConformanceTests
    {
        /// <summary>
        /// Continue conformance tests.
        /// </summary>
        public static bool Continue = true;

        /// <summary>
        /// Lock tests so they are read only one thread.
        /// </summary>
        private static object ConformanceLock = new object();

        /// <summary>
        /// Get basic tests for COSEM objects.
        /// </summary>
        /// <returns>COSEM object tests.</returns>
        private static string[] GetBasicTests()
        {
            return typeof(GXConformanceDlg).Assembly.GetManifestResourceNames()
                .Where(r => r.StartsWith("GXDLMSDirector.ConformanceTests") && r.EndsWith(".xml"))
                .ToArray();
        }

        /// <summary>
        /// Get external tests.
        /// </summary>
        /// <returns>External tests.</returns>
        private static string[] GetExternalTests(GXConformanceSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ExternalTests) ||
                !Directory.Exists(settings.ExternalTests))
            {
                return new string[0];
            }
            return Directory.GetFiles(settings.ExternalTests, "*.xml");
        }

        /// <summary>
        /// Load xml test and validate them.
        /// </summary>
        public static void ValidateTests(GXConformanceSettings settings)
        {
            //Load basic tests.
            if (!settings.ExcludeBasicTests)
            {
                List<string> tests = new List<string>(GetBasicTests());
                foreach (string it in tests)
                {
                    try
                    {
                        using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream(it))
                        {
                            GetTests(it, stream, null, null);
                            stream.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Failed to load basic test " + it + ".", ex);
                    }
                }
            }
            //Load external tests.
            GXDLMSXmlClient client = new GXDLMSXmlClient(TranslatorOutputType.SimpleXml);
            string[] list = GetExternalTests(settings);
            foreach (string it in list)
            {
                try
                {
                    using (StreamReader fs = File.OpenText(it))
                    {
                        client.Load(it);
                        fs.Close();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to load external test " + it + "." + Environment.NewLine + ex.ToString());
                }
            }
        }

        /// <summary>
        /// Get logical name as byte array.
        /// </summary>
        /// <param name="value">LN as string.</param>
        /// <returns>LN as byte array.</returns>
        static byte[] LogicalNameToBytes(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new byte[6];
            }
            string[] items = value.Split('.');
            // If data is string.
            if (items.Length != 6)
            {
                throw new ArgumentException("Invalid Logical Name");
            }
            byte[] buff = new byte[6];
            byte pos = 0;
            foreach (string it in items)
            {
                buff[pos] = Convert.ToByte(it);
                ++pos;
            }
            return buff;
        }

        /// <summary>
        /// Convert hex LN to dotted LN.
        /// </summary>
        /// <param name="ln"></param>
        /// <returns></returns>
        private static string GetLogicalName(string ln)
        {
            byte[] buff = GXCommon.HexToBytes(ln);
            return (buff[0] & 0xFF) + "." + (buff[1] & 0xFF) + "." + (buff[2] & 0xFF) + "." +
                   (buff[3] & 0xFF) + "." + (buff[4] & 0xFF) + "." + (buff[5] & 0xFF);
        }

        private static void Execute(
            GXDLMSConverter converter,
            GXConformanceTest test,
            object target,
            List<GXDLMSXmlPdu> actions,
            GXOutput output,
            GXConformanceSettings settings)
        {
            GXReplyData reply = new GXReplyData();
            string ln = null;
            int index = 0;
            ObjectType ot = ObjectType.None;
            List<KeyValuePair<ObjectType, string>> succeeded = new List<KeyValuePair<ObjectType, string>>();
            GXDLMSObject obj = null;
            if (target is GXDLMSObject)
            {
                obj = target as GXDLMSObject;
            }
            foreach (GXDLMSXmlPdu it in actions)
            {
                if (!Continue)
                {
                    break;
                }

                if (it.Command == Command.Snrm && test.Device.Comm.client.InterfaceType == InterfaceType.WRAPPER)
                {
                    continue;
                }
                if (it.Command == Command.DisconnectRequest && test.Device.Comm.client.InterfaceType == InterfaceType.WRAPPER)
                {
                    break;
                }
                //Send
                string indexStr = " attribute ";
                if (it.IsRequest())
                {
                    if (settings.Delay.TotalSeconds != 0)
                    {
                        Thread.Sleep((int)settings.Delay.TotalMilliseconds);
                    }
                    indexStr = " attribute ";
                    XmlNode i = null;
                    switch (it.Command)
                    {
                        case Command.GetRequest:
                            i = it.XmlNode.SelectNodes("GetRequestNormal")[0];
                            break;
                        case Command.SetRequest:
                            i = it.XmlNode.SelectNodes("SetRequestNormal")[0];
                            break;
                        case Command.MethodRequest:
                            i = it.XmlNode.SelectNodes("ActionRequestNormal")[0];
                            indexStr = " method ";
                            break;
                    }
                    if (i == null)
                    {
                        ot = ObjectType.None;
                        index = 0;
                        ln = null;
                    }
                    else
                    {
                        if (it.Command != Command.MethodRequest)
                        {
                            ot = (ObjectType)int.Parse(i.SelectNodes("AttributeDescriptor/ClassId")[0].Attributes["Value"].Value);
                            index = int.Parse(i.SelectNodes("AttributeDescriptor/AttributeId")[0].Attributes["Value"].Value);
                            ln = (i.SelectNodes("AttributeDescriptor/InstanceId")[0].Attributes["Value"].Value);
                            //If attribute is not implement on this version.
                            if (obj != null && index > (obj as IGXDLMSBase).GetAttributeCount())
                            {
                                break;
                            }
                            if (obj != null && (obj.GetAccess(index) & AccessMode.Read) == 0)
                            {
                                reply.Clear();
                                continue;
                            }
                        }
                        else
                        {
                            ot = (ObjectType)int.Parse(i.SelectNodes("MethodDescriptor/ClassId")[0].Attributes["Value"].Value);
                            index = int.Parse(i.SelectNodes("MethodDescriptor/MethodId")[0].Attributes["Value"].Value);
                            ln = (i.SelectNodes("MethodDescriptor/InstanceId")[0].Attributes["Value"].Value);
                            //If method is not implement on this version.
                            if (obj != null && index > (obj as IGXDLMSBase).GetMethodCount())
                            {
                                break;
                            }
                            if (obj.GetMethodAccess(index) == MethodAccessMode.NoAccess)
                            {
                                continue;
                            }
                        }
                        ln = GetLogicalName(ln);
                        test.OnTrace(test, ot + " " + ln + ":" + index + "\t");
                    }
                    reply.Clear();
                    //Skip association view and profile generic buffer.
                    if (obj != null)
                    {
                        if ((obj.ObjectType == ObjectType.AssociationLogicalName || obj.ObjectType == ObjectType.ProfileGeneric) && index == 2)
                        {
                            continue;
                        }
                    }
                    try
                    {
                        byte[][] tmp = (test.Device.Comm.client as GXDLMSXmlClient).PduToMessages(it);
                        test.Device.Comm.ReadDataBlock(tmp, "", 1, reply);
                    }
                    catch (GXDLMSException ex)
                    {
                        //Error is not shown for external tests.
                        if (obj != null)
                        {
                            if (ex.ErrorCode != 0)
                            {
                                ErrorCode e = (ErrorCode)ex.ErrorCode;
                                output.Errors.Add("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + indexStr + index + " failed: <a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.ErrorCodes?" + e + ">" + e + "</a>)");
                                test.OnTrace(test, e + "\r\n");
                            }
                            else
                            {
                                output.Errors.Add("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + indexStr + index + " <div class=\"tooltip\">failed:" + ex.Message);
                                output.Errors.Add("<span class=\"tooltiptext\">");
                                output.Errors.Add(ex.ToString());
                                output.Errors.Add("</span></div>");
                                test.OnTrace(test, ex.Message + "\r\n");
                            }
                        }
                        else
                        {
                            //Don't check result for external tests.
                            //External test might that it fails.
                        }
                    }
                    catch (Exception ex)
                    {
                        output.Errors.Add("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + indexStr + index + " <div class=\"tooltip\">failed:" + ex.Message);
                        output.Errors.Add("<span class=\"tooltiptext\">");
                        output.Errors.Add(ex.ToString());
                        output.Errors.Add("</span></div>");
                        test.OnTrace(test, ex.Message + "\r\n");
                    }
                }
                else if (reply.Data.Size != 0)
                {
                    indexStr = "Index ";
                    switch (it.Command)
                    {
                        case Command.GetResponse:
                            indexStr = "Get";
                            break;
                        case Command.SetResponse:
                            indexStr = "Set";
                            break;
                        case Command.MethodResponse:
                            indexStr = "Action";
                            break;
                    }
                    List<string> list = it.Compare(reply.ToString());
                    if (list.Count != 0)
                    {
                        //Association Logical Name attribute 4 and 6 might be also byte array.
                        if (ot == ObjectType.AssociationLogicalName && (index == 4 || index == 6) && reply.Value is byte[])
                        {
                            continue;
                        }
                        if (ot == ObjectType.None)
                        {
                            foreach (string err in list)
                            {
                                output.Errors.Add(err);
                            }
                        }
                        else
                        {
                            output.Errors.Add(" <a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + " " + indexStr + " " + index + " is <div class=\"tooltip\">invalid.");
                            output.Errors.Add("<span class=\"tooltiptext\">");
                            output.Errors.Add("Expected:</b><br/>");
                            output.Errors.Add(it.PduAsXml.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                            output.Errors.Add("<br/><b>Actual:</b><br/>");
                            output.Errors.Add(reply.ToString().Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                            output.Errors.Add("</span></div>");
                        }
                    }
                    else if (it.Command == Command.GetResponse)
                    {
                        indexStr += " Index ";
                        if (obj == null)
                        {
                            obj = GXDLMSClient.CreateObject(ot);
                            obj.LogicalName = ln;
                        }
                        ValueEventArgs e = new ValueEventArgs(obj, index, 0, null);
                        object value;
                        string name = (obj as IGXDLMSBase).GetNames()[index - 1];
                        if (target is GXDLMSAssociationLogicalName && index == 2)
                        {
                            value = reply.Value;
                        }
                        else
                        {
                            e.Value = reply.Value;
                            (obj as IGXDLMSBase).SetValue(test.Device.Comm.client.Settings, e);
                            value = obj.GetValues()[index - 1];
                        }
                        string str;
                        if (value is byte[])
                        {
                            DataType dt = obj.GetUIDataType(index);
                            if (dt == DataType.String)
                            {
                                str = ASCIIEncoding.ASCII.GetString((byte[])value);
                            }
                            else if (dt == DataType.DateTime || dt == DataType.Date || dt == DataType.Time)
                            {
                                str = GXDLMSClient.ChangeType((byte[])value, dt).ToString();
                            }
                            else
                            {
                                str = GXCommon.ToHex((byte[])value);
                            }
                        }
                        else if (value is Object[])
                        {
                            str = GXHelpers.GetArrayAsString(value);
                        }
                        else if (value is System.Collections.IList)
                        {
                            str = GXHelpers.GetArrayAsString(value);
                        }
                        else
                        {
                            str = Convert.ToString(value);
                        }
                        test.OnTrace(test, str + "\r\n");
                        if (settings.ShowValues)
                        {
                            succeeded.Add(new KeyValuePair<ObjectType, string>(ot, indexStr + index.ToString() + ":" + name + "<br/>" + str));
                        }
                        else
                        {
                            if (it.Command == Command.GetResponse && settings.ShowValues)
                            {
                                succeeded.Add(new KeyValuePair<ObjectType, string>(ot, indexStr + index.ToString()));
                            }
                            else
                            {
                                succeeded.Add(new KeyValuePair<ObjectType, string>(ot, indexStr + index.ToString()));
                            }
                        }
                    }
                    else
                    {
                        succeeded.Add(new KeyValuePair<ObjectType, string>(ot, "Test: " + Path.GetFileNameWithoutExtension((string)target) + " " + indexStr + index.ToString()));
                    }
                }
            }
            if (succeeded.Count != 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("<div class=\"tooltip\">" + ln);
                sb.Append("<span class=\"tooltiptext\">");
                foreach (var it in succeeded)
                {
                    sb.Append(it.Value + "<br/>");
                }
                sb.Append("</span></div>");
                sb.Append("&nbsp;" + converter.GetDescription(ln, succeeded[0].Key)[0] + "&nbsp;" + "<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a>.");
                output.Info.Add(sb.ToString());
            }
        }

        /// <summary>
        /// Make clone from the device.
        /// </summary>
        /// <param name="dev"></param>
        /// <returns></returns>
        public static GXDLMSDevice CloneDevice(GXDLMSDevice dev)
        {
            //Create clone from original items.
            using (MemoryStream ms = new MemoryStream())
            {
                List<Type> types = new List<Type>(GXDLMSClient.GetObjectTypes());
                types.Add(typeof(GXDLMSAttributeSettings));
                types.Add(typeof(GXDLMSAttribute));
                XmlAttributeOverrides overrides = new XmlAttributeOverrides();
                XmlAttributes attribs = new XmlAttributes();
                attribs.XmlIgnore = true;
                overrides.Add(typeof(GXDLMSDevice), "ObsoleteObjects", attribs);
                overrides.Add(typeof(GXDLMSAttributeSettings), attribs);
                XmlSerializer x = new XmlSerializer(typeof(GXDLMSDevice), overrides, types.ToArray(), null, "Gurux1");
                using (TextWriter writer = new StreamWriter(ms))
                {
                    x.Serialize(writer, dev);
                    ms.Position = 0;
                    using (XmlReader reader = XmlReader.Create(ms))
                    {
                        GXDLMSDevice dev2 = (GXDLMSDevice)x.Deserialize(reader);
                        dev2.Manufacturers = dev.Manufacturers;
                        dev = dev2;
                    }
                }
                ms.Close();
            }
            return dev;
        }

        private static void OnMessageTrace(GXDLMSDevice sender, string trace, byte[] data, int framesize, string path)
        {
            //Save highest frame size.
            if (sender.Comm.client.InterfaceType == InterfaceType.HDLC && framesize > sender.Comm.client.Limits.MaxInfoRX)
            {
                if (sender.Comm.Framesize < framesize)
                {
                    sender.Comm.Framesize = framesize;
                }
            }
            if (path != null)
            {
                using (FileStream fs = File.Open(path, FileMode.Append))
                {
                    using (TextWriter writer = new StreamWriter(fs))
                    {
                        writer.WriteLine(trace + " " + GXCommon.ToHex(data));
                    }
                }
            }
        }

        /// <summary>
        /// Load tests.
        /// </summary>
        /// <param name="stream">Stream where tests are read.</param>
        /// <param name="dev">DLMS device</param>
        /// <param name="tests">tests.</param>
        private static void GetTests(string name, Stream stream, GXDLMSDevice dev, List<KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>>> tests)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(stream);
            XmlNodeList list = doc.SelectNodes("/Messages/GetRequest/GetRequestNormal");
            ObjectType ot = ObjectType.None;
            name = name.Remove(0, "GXDLMSDirector.ConformanceTests.".Length);
            foreach (XmlNode node in list)
            {
                ot = (ObjectType)int.Parse(node.SelectNodes("AttributeDescriptor/ClassId")[0].Attributes["Value"].Value);
                if (dev != null)
                {
                    //Update logical name.
                    foreach (GXDLMSObject obj in dev.Objects.GetObjects(ot))
                    {
                        if (name.StartsWith("v0.") ||
                            name.StartsWith("v1.") ||
                            name.StartsWith("v2.") ||
                            name.StartsWith("v3."))
                        {
                            if (!name.StartsWith("v" + obj.Version + "."))
                            {
                                break;
                            }
                        }
                        string tmp = GXCommon.ToHex(LogicalNameToBytes(obj.LogicalName), false);
                        foreach (XmlNode n in list)
                        {
                            XmlAttribute ln = n.SelectNodes("AttributeDescriptor/InstanceId")[0].Attributes["Value"];
                            ln.Value = tmp;
                        }
                        if (tests != null)
                        {
                            tests.Add(new KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>>(obj, (dev.Comm.client as GXDLMSXmlClient).LoadXml(doc.InnerXml)));
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Read data from the meter.
        /// </summary>
        public static void ReadXmlMeter(object data)
        {
            object[] tmp2 = (object[])data;
            List<GXConformanceTest> tests = (List<GXConformanceTest>)tmp2[0];
            GXConformanceSettings settings = (GXConformanceSettings)tmp2[1];
            GXConformanceTest test;
            GXDLMSDevice dev = null;
            GXDLMSConverter converter = new GXDLMSConverter();
            GXOutput output;
            while (Continue)
            {
                lock (tests)
                {
                    if (tests.Count == 0)
                    {
                        return;
                    }
                    test = tests[0];
                    dev = CloneDevice(test.Device);
                    dev.InactivityTimeout = 0;
                    dev.OnTrace = OnMessageTrace;
                    dev.Comm.LogFile = Path.Combine(test.Results, "Trace.txt");
                    GXDLMSClient cl = dev.Comm.client;
                    dev.Comm.client = new GXDLMSXmlClient(TranslatorOutputType.SimpleXml);
                    cl.CopyTo(dev.Comm.client);
                    test.Device = dev;
                    output = new GXOutput(Path.Combine(test.Results, "Results.html"), dev.Name);
                    tests.RemoveAt(0);
                }
                IGXMedia media = dev.Media;
                GXDLMSXmlClient client = (GXDLMSXmlClient)dev.Comm.client;
                List<string> files = new List<string>();
                DateTime start = DateTime.Now;
                try
                {
                    output.PreInfo.Add("<a target=\"_blank\" href=\"https://www.gurux.fi/gurux.dlms.ctt.tests\">Gurux Conformance Tests</a>");

                    output.PreInfo.Add("Start Time: " + start.ToString());
                    output.PreInfo.Add("<hr>");
                    media.Open();
                    dev.InitializeConnection();
                    if (dev.MaxInfoRX != 128 && dev.MaxInfoRX != dev.Comm.client.Limits.MaxInfoRX)
                    {
                        output.Warnings.Add("Client asked that RX frame size is " + dev.MaxInfoRX + ". Meter uses " + dev.Comm.client.Limits.MaxInfoRX);
                    }
                    if (dev.MaxInfoTX != 128 && dev.MaxInfoTX != dev.Comm.client.Limits.MaxInfoTX)
                    {
                        output.Warnings.Add("Client asked that TX frame size is " + dev.MaxInfoTX + ". Meter uses " + dev.Comm.client.Limits.MaxInfoTX);
                    }
                    if (dev.PduSize < dev.Comm.client.MaxReceivePDUSize)
                    {
                        output.Warnings.Add("Client asked that PDU size is " + dev.PduSize + ". Meter uses " + dev.Comm.client.MaxReceivePDUSize);
                    }
                    int maxframesize = dev.Comm.client.Limits.MaxInfoRX;
                    if (settings.ReReadAssociationView)
                    {
                        test.OnTrace(test, "Re-reading association view.\r\n");
                        dev.Objects.Clear();
                        dev.Objects.AddRange(dev.Comm.GetObjects());
                    }
                    if (client.UseLogicalNameReferencing)
                    {
                        output.PreInfo.Add("Testing using Logical Name referencing.");
                    }
                    else
                    {
                        output.PreInfo.Add("Testing using Short Name referencing.");
                    }
                    output.PreInfo.Add("Authentication level: " + dev.Authentication);
                    StringBuilder sb = new StringBuilder();
                    foreach (Conformance it in Enum.GetValues(typeof(Conformance)))
                    {
                        if (((int)it & (int)client.ProposedConformance) != 0)
                        {
                            sb.Append("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Conformance?" + it + ">" + it + "</a>, ");
                        }
                    }
                    if (sb.Length != 0)
                    {
                        sb.Length -= 2;
                    }
                    output.PreInfo.Add("Proposed services:");
                    output.PreInfo.Add(sb.ToString());
                    sb.Clear();
                    foreach (Conformance it in Enum.GetValues(typeof(Conformance)))
                    {
                        if (((int)it & (int)client.NegotiatedConformance) != 0)
                        {
                            sb.Append("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Conformance?" + it + ">" + it + "</a>, ");
                        }
                    }
                    if (sb.Length != 0)
                    {
                        sb.Length -= 2;
                    }
                    output.PreInfo.Add("Supported services:");
                    output.PreInfo.Add(sb.ToString());

                    if (!settings.ExcludeBasicTests)
                    {
                        output.PreInfo.Add("Total amount of objects: " + dev.Objects.Count.ToString());
                        //Check OBIS codes.
                        foreach (GXDLMSObject it in dev.Objects)
                        {
                            if (it.Description == "Invalid")
                            {
                                output.Errors.Add("Invalid OBIS code " + it.LogicalName + " for <a target=\"_blank\" href=http://www.gurux.fi/" + it.GetType().FullName + ">" + it.ObjectType + "</a>.");
                                Console.WriteLine("------------------------------------------------------------");
                                Console.WriteLine(it.LogicalName + ": Invalid OBIS code.");
                            }
                        }
                    }
                    GXDLMSData ldn = new GXDLMSData("0.0.42.0.0.255");
                    try
                    {
                        dev.Comm.ReadValue(ldn, 2);
                        object v = ldn.Value;
                        if (v is byte[])
                        {
                            v = ASCIIEncoding.ASCII.GetString((byte[])v);
                        }
                        output.PreInfo.Add("Logical Device Name is: " + Convert.ToString(v + "."));
                    }
                    catch (Exception)
                    {
                        output.Errors.Add("Logical Device Name is not implemented.");
                    }
                    GXDLMSData firmware = new GXDLMSData("1.0.0.2.0.255");
                    try
                    {
                        dev.Comm.ReadValue(firmware, 2);
                        object v = firmware.Value;
                        if (v is byte[])
                        {
                            v = ASCIIEncoding.ASCII.GetString((byte[])v);
                        }
                        output.PreInfo.Add("Firmware version is: " + Convert.ToString(v) + ".");
                    }
                    catch (Exception)
                    {
                        output.Info.Add("Firmware version is not available.");
                    }
                    GXDLMSClock time = new GXDLMSClock("0.0.1.0.0.255");
                    try
                    {
                        dev.Comm.ReadValue(time, 2);
                        output.PreInfo.Add("Meter time: " + Convert.ToString(time.Time) + ".");
                    }
                    catch (Exception)
                    {
                        //It's OK if this fails.
                    }

                    //Read structures of Cosem objects.
                    List<KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>>> cosemTests = new List<KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>>>();
                    List<KeyValuePair<string, List<GXDLMSXmlPdu>>> externalTests = new List<KeyValuePair<string, List<GXDLMSXmlPdu>>>();
                    GXDLMSTranslator translator = new GXDLMSTranslator(TranslatorOutputType.SimpleXml);
                    lock (ConformanceLock)
                    {
                        //Load basic tests.
                        if (!settings.ExcludeBasicTests)
                        {
                            foreach (string it in GetBasicTests())
                            {
                                using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream(it))
                                {
                                    GetTests(it, stream, dev, cosemTests);
                                    stream.Close();
                                }
                            }
                        }
                        else
                        {
                            test.OnTrace(test, "Basic tests are ignored.\r\n");
                            output.PreInfo.Add("Basic tests are ignored.");
                        }
                        //Load external tests.
                        string[] list = GetExternalTests(settings);
                        if (list.Length != 0)
                        {
                            string dir = Path.Combine(test.Results, "External");
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            string str = "External tests: " + list.Length;
                            test.OnTrace(test, str + ".\r\n");
                            output.PreInfo.Add(str);
                            foreach (string it in list)
                            {
                                try
                                {
                                    using (StreamReader fs = File.OpenText(it))
                                    {
                                        externalTests.Add(new KeyValuePair<string, List<GXDLMSXmlPdu>>(it, client.Load(fs)));
                                        fs.Close();
                                    }
                                    File.Copy(it, Path.Combine(dir, Path.GetFileName(it)));
                                }
                                catch (Exception e)
                                {
                                    string errStr = "Failed to load exteranal test " + it + ". " + e.Message;
                                    output.Errors.Add(errStr);
                                    test.OnTrace(test, errStr + "\r\n");
                                }

                            }
                        }
                    }
                    if (settings.Amount != 1)
                    {
                        string str = "Tests are run " + settings.Amount + " times.\r\n";
                        test.OnTrace(test, str);
                        output.PreInfo.Add(str);
                    }

                    for (int pos = 0; pos != settings.Amount; ++pos)
                    {
                        int i = 0, cnt = cosemTests.Count;
                        foreach (KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>> it in cosemTests)
                        {
                            if (!Continue)
                            {
                                break;
                            }
                            try
                            {
                                test.OnProgress(test, "Testing " + it.Key.LogicalName, ++i, cnt);
                                Execute(converter, test, it.Key, it.Value, output, settings);
                            }
                            catch (Exception ex)
                            {
                                test.OnError(test, ex);
                            }
                        }
                        i = 0;
                        cnt = externalTests.Count;
                        foreach (KeyValuePair<string, List<GXDLMSXmlPdu>> it in externalTests)
                        {
                            if (!Continue)
                            {
                                break;
                            }
                            try
                            {
                                test.OnProgress(test, "Testing " + it.Key, ++i, cnt);
                                Execute(converter, test, it.Key, it.Value, output, settings);
                            }
                            catch (Exception ex)
                            {
                                test.OnError(test, ex);
                            }
                        }
                        //Check this only once.
                        if (!settings.ExcludeBasicTests && pos == 0)
                        {
                            List<ObjectType> unknownDataTypes = new List<ObjectType>();
                            foreach (GXDLMSObject o in dev.Objects)
                            {
                                if (!unknownDataTypes.Contains(o.ObjectType))
                                {
                                    bool found = false;
                                    foreach (KeyValuePair<GXDLMSObject, List<GXDLMSXmlPdu>> t in cosemTests)
                                    {
                                        if (o.ObjectType == t.Key.ObjectType)
                                        {
                                            found = true;
                                            break;
                                        }
                                    }
                                    if (!found)
                                    {
                                        unknownDataTypes.Add(o.ObjectType);
                                        output.Warnings.Add("<a target=\"_blank\" href=http://www.gurux.fi/" + o.GetType().FullName + ">" + o.ObjectType + "</a> is not tested.");
                                    }
                                }
                            }
                        }
                        if (settings.Write)
                        {
                            test.OnTrace(test, "Write tests started\r\n");
                            foreach (GXDLMSObject obj in dev.Objects)
                            {
                                for (int index = 1; index != (obj as IGXDLMSBase).GetAttributeCount(); ++index)
                                {
                                    if ((obj.GetAccess(index) & AccessMode.Read) != 0 && (obj.GetAccess(index) & AccessMode.Write) != 0)
                                    {
                                        ObjectType ot = obj.ObjectType;
                                        string ln = obj.LogicalName;
                                        try
                                        {
                                            test.OnTrace(test, ot + " " + ln + ":" + index + "\r\n");
                                            object expected = obj.GetValues()[index - 1];
                                            dev.Comm.Write(obj, index);
                                            object actual = obj.GetValues()[index - 1];
                                            //Check that value is not changed.
                                            if (Convert.ToString(expected) != Convert.ToString(actual))
                                            {
                                                output.Errors.Add("Write <a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + " attribute " + index + " is <div class=\"tooltip\">failed.");
                                                output.Errors.Add("<span class=\"tooltiptext\">");
                                                output.Errors.Add("Expected:</b><br/>");
                                                output.Errors.Add(Convert.ToString(expected).Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                                                output.Errors.Add("<br/><b>Actual:</b><br/>");
                                                output.Errors.Add(Convert.ToString(actual).Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                                                output.Errors.Add("</span></div>");
                                            }
                                            else
                                            {
                                                output.Info.Add("Write" + ot + " " + ln + " attribute " + index + " Succeeded.");
                                            }
                                        }
                                        catch (GXDLMSException ex)
                                        {
                                            if (ex.ErrorCode != 0)
                                            {
                                                ErrorCode e = (ErrorCode)ex.ErrorCode;
                                                output.Errors.Add("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + " attribute " + index + " failed: <a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.ErrorCodes?" + e + ">" + e + "</a>)");
                                            }
                                            else
                                            {
                                                output.Errors.Add("<a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + " attribute " + index + " <div class=\"tooltip\">failed:" + ex.Message);
                                                output.Errors.Add("<span class=\"tooltiptext\">");
                                                output.Errors.Add(ex.ToString().Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                                                output.Errors.Add("</span></div>");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            output.Errors.Add("Write <a target=\"_blank\" href=http://www.gurux.fi/Gurux.DLMS.Objects.GXDLMS" + ot + ">" + ot + "</a> " + ln + " attribute " + index + " <div class=\"tooltip\">failed. " + ex.Message);
                                            output.Errors.Add("<span class=\"tooltiptext\">");
                                            output.Errors.Add(ex.ToString().Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>"));
                                            output.Errors.Add("</span></div>");
                                        }
                                    }
                                }
                            }
                        }
                        //Test invalid password.
                        TestInvalidPassword(settings, dev, output);
                        TestImageTransfer(settings, test, dev, output);
                    }
                    if (!settings.ExcludeBasicTests)
                    {
                        TestAssociationLn(settings, dev, output);
                    }

                    if (dev.Comm.Framesize != 0)
                    {
                        output.Errors.Insert(0, "HDLC frame size is is too high. There are " + dev.Comm.Framesize + " bytes. Max size should be max " + dev.Comm.client.Limits.MaxInfoRX + " bytes.");
                    }
                    if (output.Errors.Count != 0)
                    {
                        test.ErrorLevel = 2;
                    }
                    else if (output.Warnings.Count != 0)
                    {
                        test.ErrorLevel = 1;
                    }
                    else
                    {
                        test.ErrorLevel = 0;
                    }
                    test.OnReady(test);
                }
                catch (Exception ex)
                {
                    test.OnError(test, ex);
                }
                finally
                {
                    output.PreInfo.Insert(1, "Ran for " + (DateTime.Now - start).ToString());
                    output.MakeReport();
                    output.writer.Flush();
                    output.writer.Close();
                    if (dev != null)
                    {
                        dev.Comm.Disconnect();
                    }
                    if (test.Done != null)
                    {
                        test.Done.Set();
                    }
                }
            }
        }

        /// <summary>
        /// Test current association.
        /// </summary>
        /// <param name="settings">Conformance settings.</param>
        /// <param name="dev">DLMS device.</param>
        /// <param name="output"></param>
        private static void TestAssociationLn(GXConformanceSettings settings, GXDLMSDevice dev, GXOutput output)
        {
            GXDLMSAssociationLogicalName ln = (GXDLMSAssociationLogicalName)dev.Comm.client.Objects.FindByLN(ObjectType.AssociationLogicalName, "0.0.40.0.0.255");
            if (ln == null)
            {
                if (ln == null)
                {
                    GXDLMSObjectCollection objects = dev.Comm.client.Objects.GetObjects(ObjectType.AssociationLogicalName);
                    foreach (GXDLMSAssociationLogicalName it in objects)
                    {
                        if (it.AuthenticationMechanismName.MechanismId == dev.Authentication)
                        {
                            ln = (GXDLMSAssociationLogicalName)it;
                            break;
                        }
                    }
                }
                if (ln == null)
                {
                    ln = new GXDLMSAssociationLogicalName("0.0.40.0.0.255");
                }
            }
            //Read values if not read yet.
            if (settings.ExcludeBasicTests)
            {
                dev.Comm.ReadValue(ln, 4);
                dev.Comm.ReadValue(ln, 5);
                dev.Comm.ReadValue(ln, 6);
                dev.Comm.ReadValue(ln, 8);
            }
            if (ln.XDLMSContextInfo.DlmsVersionNumber != 6)
            {
                output.Errors.Insert(0, "Invalid DLMS version: " + ln.ApplicationContextName.DlmsUA);
            }

            if (ln.ApplicationContextName.JointIsoCtt != 2 && ln.ApplicationContextName.JointIsoCtt != 96)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.JointIsoCtt: " + ln.ApplicationContextName.JointIsoCtt);
            }
            if (ln.ApplicationContextName.Country != 16 && ln.ApplicationContextName.Country != 133)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.Country: " + ln.ApplicationContextName.Country);
            }
            if (ln.ApplicationContextName.CountryName != 756 && ln.ApplicationContextName.CountryName != 116)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.CountryName: " + ln.ApplicationContextName.CountryName);
            }
            if (ln.ApplicationContextName.IdentifiedOrganization != 5)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.IdentifiedOrganization: " + ln.ApplicationContextName.IdentifiedOrganization);
            }
            if (ln.ApplicationContextName.DlmsUA != 8)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.DlmsUA: " + ln.ApplicationContextName.DlmsUA);
            }
            if (ln.ApplicationContextName.ApplicationContext != 1)
            {
                output.Errors.Insert(0, "Wrong ApplicationContextName.ApplicationContext: " + ln.ApplicationContextName.ApplicationContext);
            }
            if (dev.Comm.client.UseLogicalNameReferencing)
            {
                if (dev.Comm.client.Ciphering.Security == Security.None && ln.ApplicationContextName.ContextId != ApplicationContextName.LogicalName)
                {
                    output.Errors.Insert(0, "Wrong ApplicationContextName.ContextId: " + ln.ApplicationContextName.ContextId);
                }
                else if (dev.Comm.client.Ciphering.Security != Security.None && ln.ApplicationContextName.ContextId != ApplicationContextName.LogicalNameWithCiphering)
                {
                    output.Errors.Insert(0, "Wrong ApplicationContextName.ContextId: " + ln.ApplicationContextName.ContextId);
                }
            }
            else
            {
                if (dev.Comm.client.Ciphering.Security == Security.None && ln.ApplicationContextName.ContextId != ApplicationContextName.ShortName)
                {
                    output.Errors.Insert(0, "Wrong ApplicationContextName.ContextId: " + ln.ApplicationContextName.ContextId);
                }
                else if (dev.Comm.client.Ciphering.Security != Security.None && ln.ApplicationContextName.ContextId != ApplicationContextName.ShortNameWithCiphering)
                {
                    output.Errors.Insert(0, "Wrong ApplicationContextName.ContextId: " + ln.ApplicationContextName.ContextId);
                }
            }

            if (ln.AuthenticationMechanismName.JointIsoCtt != 2 && ln.AuthenticationMechanismName.JointIsoCtt != 96)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.JointIsoCtt: " + ln.AuthenticationMechanismName.JointIsoCtt);
            }
            if (ln.AuthenticationMechanismName.Country != 16 && ln.AuthenticationMechanismName.Country != 133)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.Country: " + ln.AuthenticationMechanismName.Country);
            }
            if (ln.AuthenticationMechanismName.CountryName != 756 && ln.AuthenticationMechanismName.CountryName != 116)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.CountryName: " + ln.AuthenticationMechanismName.CountryName);
            }
            if (ln.AuthenticationMechanismName.IdentifiedOrganization != 5)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.IdentifiedOrganization: " + ln.AuthenticationMechanismName.IdentifiedOrganization);
            }
            if (ln.AuthenticationMechanismName.DlmsUA != 8)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.DlmsUA: " + ln.AuthenticationMechanismName.DlmsUA);
            }
            if (ln.AuthenticationMechanismName.AuthenticationMechanismName != 2)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.AuthenticationMechanismName:0 " + ln.AuthenticationMechanismName.AuthenticationMechanismName);
            }
            if (ln.AuthenticationMechanismName.MechanismId != dev.Comm.client.Authentication)
            {
                output.Errors.Insert(0, "Wrong AuthenticationMechanismName.MechanismId: " + ln.AuthenticationMechanismName.MechanismId);
            }

            if (ln.AssociationStatus != Gurux.DLMS.Objects.Enums.AssociationStatus.Associated)
            {
                output.Errors.Insert(0, "Invalid AssociationStatus: " + ln.AssociationStatus);
            }
            if (ln.XDLMSContextInfo.Conformance == Conformance.None)
            {
                output.Errors.Insert(0, "Invalid Conformance: " + ln.XDLMSContextInfo.Conformance);
            }
            if (ln.XDLMSContextInfo.MaxReceivePduSize == 0)
            {
                output.Errors.Insert(0, "Invalid MaxReceivePduSize: " + ln.XDLMSContextInfo.MaxReceivePduSize);
            }
            if (ln.XDLMSContextInfo.MaxSendPduSize == 0)
            {
                output.Errors.Insert(0, "Invalid MaxSendPduSize: " + ln.XDLMSContextInfo.MaxSendPduSize);
            }
        }

        /// <summary>
        /// Test that meter can handle invalid password.
        /// </summary>
        /// <param name="settings">Conformance settings.</param>
        /// <param name="dev">DLMS device.</param>
        /// <param name="output"></param>
        private static void TestInvalidPassword(GXConformanceSettings settings, GXDLMSDevice dev, GXOutput output)
        {
            if (!string.IsNullOrEmpty(settings.InvalidPassword) && dev.Comm.client.Authentication != Authentication.None)
            {
                dev.Comm.Disconnect();
                string pw = dev.Password;
                byte[] hpw = dev.HexPassword;
                dev.Password = CryptHelper.Encrypt(settings.InvalidPassword, Password.Key);
                dev.HexPassword = null;
                try
                {
                    Thread.Sleep((int)settings.DelayConnection.TotalMilliseconds);
                    dev.InitializeConnection();
                    output.Errors.Insert(0, "Login succeeded with wrong password.");
                    dev.Comm.Disconnect();
                }
                catch (GXDLMSException)
                {
                    output.Info.Insert(0, "Invalid password test succeeded.");
                    dev.Comm.Disconnect();
                }
                //Try to connect again.
                dev.Password = pw;
                dev.HexPassword = hpw;
                try
                {
                    Thread.Sleep((int)settings.DelayConnection.TotalMilliseconds);
                    dev.InitializeConnection();
                }
                catch (GXDLMSException)
                {
                    output.Errors.Insert(0, "Login failed after wrong password.");
                }
            }
        }

        /// <summary>
        /// Test image transfer.
        /// </summary>
        /// <param name="settings">Conformance settings.</param>
        /// <param name="dev">DLMS device.</param>
        /// <param name="output"></param>
        private static void TestImageTransfer(GXConformanceSettings settings, GXConformanceTest test, GXDLMSDevice dev, GXOutput output)
        {          
            if (!string.IsNullOrEmpty(settings.ImageFile) && settings.ImageIdentifier != null && settings.ImageIdentifier.Length != 0)
            {
                GXDLMSObjectCollection objects = dev.Comm.client.Objects.GetObjects(ObjectType.ImageTransfer);
                if (objects.Count != 0)
                {
                    output.PreInfo.Add("Testing Image transfer.");
                    GXDLMSImageTransfer img = (GXDLMSImageTransfer)objects[0];
                    dev.Comm.ReadValue(img, 5);
                    if (!img.ImageTransferEnabled)
                    {
                        output.Errors.Insert(0, "Image transfer is not enabled.");
                    }
                    else
                    {
                        //Step 1. BB: 4.4.6.4
                        dev.Comm.ReadValue(img, 2);
                        output.Info.Add("Image block size is " + img.ImageBlockSize + " bytes.");
                        byte[] image = null;
                        if (string.Compare(Path.GetExtension(settings.ImageFile), ".xml", true) == 0)
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(settings.ImageFile);
                            GXImageDlg.GetImage(doc.ChildNodes, ref image);
                        }
                        else
                        {
                            image = File.ReadAllBytes(settings.ImageFile);
                        }
                        //Step 2. BB: 4.4.6.4
                        bool error = false;
                        GXReplyData reply = new GXReplyData();
                        dev.Comm.ReadDataBlock(img.ImageTransferInitiate(dev.Comm.client, settings.ImageIdentifier, image.Length), "", 1, reply);
                        reply.Clear();

                        //Check ImageTransferredBlocksStatus.
                        dev.Comm.ReadValue(img, 3);
                        if (img.ImageTransferredBlocksStatus != null)
                        {
                            foreach (char it in img.ImageTransferredBlocksStatus)
                            {
                                if (it != '0')
                                {
                                    error = true;
                                    output.Errors.Add("Image transferred blocks status is wrong. It's " + img.ImageTransferredBlocksStatus + " and it shoud be zilled with 0.");
                                }
                            }
                        }

                        //Check ImageTransferStatus.
                        dev.Comm.ReadValue(img, 6);
                        if (img.ImageTransferStatus != ImageTransferStatus.TransferInitiated)
                        {
                            error = true;
                            output.Errors.Add("Image transfer status is wrong. It's " + img.ImageTransferStatus + " and it shoud be TransferInitiated.");
                        }
                        //Check ImageFirstNotTransferredBlockNumber.
                        dev.Comm.ReadValue(img, 4);
                        if (img.ImageFirstNotTransferredBlockNumber != 0)
                        {
                            error = true;
                            output.Errors.Add("Image first not transferred block number wrong. It's " + img.ImageFirstNotTransferredBlockNumber + " and it shoud be 0.");
                        }
                        //Check ImageActivateInfo.
                        dev.Comm.ReadValue(img, 7);
                        if (img.ImageActivateInfo != null && img.ImageActivateInfo.Length != 0)
                        {
                            error = true;
                            output.Errors.Add("Image activate info is not reset.");
                        }
                        if (!error)
                        {
                            output.Info.Add("Image activation Step 2 succeeded.");
                        }
                        //Step 3. BB: 4.4.6.4
                        DateTime start = DateTime.Now;
                        error = false;
                        byte[][] blocks = img.GetImageBlocks(image);
                        int pos = 0, cnt = blocks.Length;
                        reply.Clear();
                        foreach (byte[] b in blocks)
                        {
                            dev.Comm.ReadDataBlock(dev.Comm.client.Method(img, 2, b, DataType.Array), "", 1, reply);
                            if (reply.Error != 0)
                            {
                                output.Errors.Insert(0, "Image transfer failed. Error code: " + reply.Error);
                                return;
                            }
                            reply.Clear();
                            //Check ImageTransferredBlocksStatus.
                            dev.Comm.ReadValue(img, 3);
                            if (img.ImageTransferredBlocksStatus == null)
                            {
                                error = true;
                                output.Errors.Add("Image Transferred blocks status is not implemented.");
                            }
                            else
                            {
                                if (img.ImageTransferredBlocksStatus.Length < pos)
                                {
                                    error = true;
                                    output.Errors.Add("Image Transferred blocks status is wrong. Amount of bits is different than block size. (" + img.ImageTransferredBlocksStatus.Length + "/" + blocks.Length + ")");
                                }
                                else
                                {
                                    if (img.ImageTransferredBlocksStatus[pos] != '1')
                                    {
                                        error = true;
                                        output.Errors.Add("Image transferred blocks status is wrong. It's " + img.ImageFirstNotTransferredBlockNumber + " and it shoud be " + blocks.Length + ".");
                                    }
                                }
                            }
                            test.OnProgress(test, "Image block transfer...", ++pos, cnt);
                        }
                        if (!error)
                        {
                            output.Info.Add("Image transfer (Step 3) succeeded.");
                            output.Info.Add("Image transfer takes " + (DateTime.Now - start).ToString(@"hh\:mm\:ss"));
                        }
                        //Step 4. BB: 4.4.6.4
                        error = false;
                        test.OnProgress(test, "Checing completeness of the Image...", 1, 1);
                        //Check ImageFirstNotTransferredBlockNumber.
                        dev.Comm.ReadValue(img, 4);
                        if (img.ImageFirstNotTransferredBlockNumber != blocks.Length)
                        {
                            error = true;
                            output.Errors.Add("Image first not transferred block number wrong. It's " + img.ImageFirstNotTransferredBlockNumber + " and it shoud be " + blocks.Length + ".");
                        }
                        //Check ImageTransferredBlocksStatus.
                        dev.Comm.ReadValue(img, 3);
                        if (img.ImageTransferredBlocksStatus == null)
                        {
                            error = true;
                            output.Errors.Add("Image Transferred blocks status is not implemented.");
                        }
                        else
                        {
                            if (img.ImageTransferredBlocksStatus.Length != blocks.Length)
                            {
                                error = true;
                                output.Errors.Add("Image Transferred blocks status is wrong. Amount of bits is different than block size. (" + img.ImageTransferredBlocksStatus.Length + "/" + blocks.Length + ")");
                            }
                            foreach (char it in img.ImageTransferredBlocksStatus)
                            {
                                if (it != '1')
                                {
                                    error = true;
                                    output.Errors.Add("Image transferred blocks status is wrong. It's " + img.ImageFirstNotTransferredBlockNumber + " and it shoud be " + blocks.Length + ".");
                                }
                            }
                        }
                        if (!error)
                        {
                            output.Info.Add("Image completeness Step 4 succeeded.");
                        }

                        if (settings.ImageVerify)
                        {
                            start = DateTime.Now;
                            //Step 5. BB: 4.4.6.4
                            do
                            {
                                reply.Clear();
                                test.OnProgress(test, "Verifying image...", 1, 1);
                                try
                                {
                                    dev.Comm.ReadDataBlock(img.ImageVerify(dev.Comm.client), "", 1, reply);
                                }
                                catch (GXDLMSException ex)
                                {
                                    reply.Error = (short)ex.ErrorCode;
                                }
                                if (reply.Error == (short)ErrorCode.TemporaryFailure)
                                {
                                    test.OnProgress(test, "Check is image verify ready...", 1, 1);
                                    dev.Comm.ReadValue(img, 6);
                                    if (img.ImageTransferStatus == ImageTransferStatus.VerificationInitiated)
                                    {
                                        test.OnProgress(test, "Still verifying...", 1, 1);
                                    }
                                    Thread.Sleep((int)settings.ImageVerifyWaitTime.TotalMilliseconds);
                                }
                                else if (reply.Error != 0)
                                {
                                    output.Errors.Insert(0, "Image verification failed. Error code: " + reply.Error);
                                    return;
                                }
                            } while (reply.Error != 0);
                            output.Info.Add("Image verify succeeded (Step 5).");
                            output.Info.Add("Verify takes " + (DateTime.Now - start).ToString(@"hh\:mm\:ss"));
                        }
                        reply.Clear();
                        if (settings.ImageActivate)
                        {
                            start = DateTime.Now;
                            //Step 7. BB: 4.4.6.4
                            do
                            {
                                test.OnProgress(test, "Activating image...", 1, 1);
                                try
                                {
                                    reply.Clear();
                                    dev.Comm.ReadDataBlock(img.ImageActivate(dev.Comm.client), "", 1, reply);
                                }
                                catch (GXDLMSException ex)
                                {
                                    reply.Error = (short)ex.ErrorCode;
                                }
                                if (reply.Error == (short)ErrorCode.TemporaryFailure)
                                {
                                    test.OnProgress(test, "Check is image activation ready...", 1, 1);
                                    dev.Comm.ReadValue(img, 6);
                                    if (img.ImageTransferStatus == ImageTransferStatus.ActivationInitiated)
                                    {
                                        test.OnProgress(test, "Still activating...", 1, 1);
                                    }
                                    Thread.Sleep((int)settings.ImageActivateWaitTime.TotalMilliseconds);
                                }
                                else if (reply.Error != 0)
                                {
                                    output.Errors.Insert(0, "Image activation failed. Error code: " + reply.Error);
                                    return;
                                }
                            } while (reply.Error != 0);
                            output.Info.Add("Image activation succeeded (Step 6).");
                            output.Info.Add("Activation takes " + (DateTime.Now - start).ToString(@"hh\:mm\:ss"));

                        }
                    }
                }
            }
        }
    }
}
