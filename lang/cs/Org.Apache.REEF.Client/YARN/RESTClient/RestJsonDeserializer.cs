﻿// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Deserializers;

namespace Org.Apache.REEF.Client.YARN.RestClient
{
    internal sealed class RestJsonDeserializer : IDeserializer
    {
        public string RootElement { get; set; }
        public string Namespace { get; set; }
        public string DateFormat { get; set; }

        public T Deserialize<T>(IRestResponse response)
        {
            /* If root element is not empty, then we want to 
             * skip the top level token and parse only one level deeper
             * For instance:
             * {
             *     "app" : {
             *        "state" : "FINISHED",
             *        "user" : "user1",
             *     }
             *  }
             * when used without this code will need an extra wrapper class
             * around a class that has `state` and `user` fields.
             * 
             * public class AppResponse
             * {
             *      public Application App { get;set }
             * }
             * 
             * This logic helps us avoid such classes.
            */ 
            if (!string.IsNullOrEmpty(RootElement))
            {
                var jobject = JObject.Parse(response.Content);
                var jtoken = jobject[RootElement];
                var jsonSerializer = new JsonSerializer();
                jsonSerializer.Converters.Add(new StringEnumConverter());
                return jtoken.ToObject<T>(jsonSerializer);
            }

            return JsonConvert.DeserializeObject<T>(response.Content, new StringEnumConverter());
        }
    }
}