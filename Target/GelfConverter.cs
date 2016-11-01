﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using NLog;
using Newtonsoft.Json.Linq;

namespace Gelf4NLog.Target
{
    public class GelfConverter : IConverter
    {
        private const int ShortMessageMaxLength = 250;
        private const string GelfVersion = "1.1";

        public JObject GetGelfJson(LogEventInfo logEventInfo, string facility)
        {
            //Retrieve the formatted message from LogEventInfo
            var logEventMessage = logEventInfo.FormattedMessage;
            if (logEventMessage == null) return null;

            //Construct the instance of GelfMessage
            //See http://docs.graylog.org/en/2.1/pages/gelf.html?highlight=short%20message#gelf-format-specification "Specification (version 1.1)"
            var gelfMessage = new GelfMessage
            {
                Version = GelfVersion,
                Host = Dns.GetHostName(),
                ShortMessage = GetShortMessage(logEventMessage),
                FullMessage = logEventMessage,
                Timestamp = logEventInfo.TimeStamp,
                Level = GetSeverityLevel(logEventInfo.Level)
            };

            //Convert to JSON
            var jsonObject = JObject.FromObject(gelfMessage);

            //Add any other interesting data to additional fields
            AddAdditionalField(jsonObject, new KeyValuePair<object, object>("facility", facility));
            AddAdditionalField(jsonObject, new KeyValuePair<object, object>("line", logEventInfo.UserStackFrame?.GetFileLineNumber().ToString(CultureInfo.InvariantCulture)));
            AddAdditionalField(jsonObject, new KeyValuePair<object, object>("file", logEventInfo.UserStackFrame?.GetFileName()));
            AddAdditionalField(jsonObject, new KeyValuePair<object, object>("LoggerName", logEventInfo.LoggerName));
            AddAdditionalField(jsonObject, new KeyValuePair<object, object>("LogLevelName", logEventInfo.Level.ToString()));

            //If we are dealing with an exception, add exception properties as additional fields
            if (logEventInfo.Exception != null)
            {
                AddAdditionalField(jsonObject, new KeyValuePair<object, object>("ExceptionSource", logEventInfo.Exception.Source));
                AddAdditionalField(jsonObject, new KeyValuePair<object, object>("ExceptionMessage", logEventInfo.Exception.Message));
                AddAdditionalField(jsonObject, new KeyValuePair<object, object>("StackTrace", logEventInfo.Exception.StackTrace));
            }

            //We will persist them "Additional Fields" according to Gelf spec
            foreach (var property in logEventInfo.Properties)
            {
                AddAdditionalField(jsonObject, property);
            }

            return jsonObject;
        }

        private static string GetShortMessage(string logEventMessage)
        {
            //Figure out the short message
            var shortMessage = logEventMessage;
            if (shortMessage.Length > ShortMessageMaxLength)
            {
                shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
            }
            return shortMessage;
        }

        /// <summary>
        /// Values from SyslogSeverity enum here: http://marc.info/?l=log4net-dev&m=109519564630799
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        private static int GetSeverityLevel(LogLevel level)
        {
            if (level == LogLevel.Debug)
            {
                return 7;
            }
            if (level == LogLevel.Fatal)
            {
                return 2;
            }
            if (level == LogLevel.Info)
            {
                return 6;
            }
            if (level == LogLevel.Trace)
            {
                return 6;
            }
            if (level == LogLevel.Warn)
            {
                return 4;
            }

            return 3; //LogLevel.Error
        }

        private static void AddAdditionalField(IDictionary<string, JToken> jObject, KeyValuePair<object, object> property)
        {
            var key = property.Key as string;
            var value = property.Value as string;

            if (key == null) return;

            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                key = "id_";

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            jObject.Add(key, value);
        }
    }
}
