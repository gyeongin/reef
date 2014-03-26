/**
 * Copyright (C) 2013 Microsoft Corporation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *         http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package com.microsoft.reef.io.network.naming.serialization;

import com.google.protobuf.InvalidProtocolBufferException;
import com.microsoft.io.network.naming.avro.AvroNamingLookupRequest;
import com.microsoft.reef.io.network.naming.exception.NamingRuntimeException;
import com.microsoft.reef.io.network.proto.ReefNetworkNamingProtos.NamingLookupRequestPBuf;
import com.microsoft.wake.Identifier;
import com.microsoft.wake.IdentifierFactory;
import com.microsoft.wake.remote.Codec;

import java.util.ArrayList;
import java.util.List;

/**
 * Naming lookup request codec
 */
public class NamingLookupRequestCodec implements Codec<NamingLookupRequest> {

  private final IdentifierFactory factory;

  /**
   * Constructs a naming lookup request codec
   *
   * @param factory the identifier factory
   */
  public NamingLookupRequestCodec(IdentifierFactory factory) {
    this.factory = factory;
  }

  /**
   * Encodes the identifiers to bytes
   *
   * @param obj the naming lookup request
   * @return a byte array
   */
  @Override
  public byte[] encode(NamingLookupRequest obj) {
    AvroNamingLookupRequest.Builder builder = AvroNamingLookupRequest.newBuilder();
    List<CharSequence> ids = new ArrayList<>();
    for (Identifier id : obj.getIdentifiers()) {
      ids.add(id.toString());
    }
    builder.setIds(ids);
    return AvroUtils.toBytes(builder.build(), AvroNamingLookupRequest.class);
  }

  /**
   * Decodes the bytes to a naming lookup request
   *
   * @param buf the byte array
   * @return a naming lookup request
   */
  @Override
  public NamingLookupRequest decode(byte[] buf) {
    AvroNamingLookupRequest req = AvroUtils.fromBytes(buf, AvroNamingLookupRequest.class);

    List<Identifier> ids = new ArrayList<Identifier>();
    for (CharSequence s : req.getIds()) {
      ids.add(factory.getNewInstance(s.toString()));
    }
    return new NamingLookupRequest(ids);
  }

}
