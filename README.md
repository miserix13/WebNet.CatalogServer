# WebNet.CatalogServer

## A document / catalog (as in a collection of related items) database server built on .NET 10 and written in C#

## For storage, uses <https://github.com/koculu/ZoneTree>, <https://github.com/stonstad/Stellar.FastDB> and <https://github.com/curiosity-ai/rocksdb-sharp> key-value stores for both documents and catalogs

## Uses <https://github.com/MessagePack-CSharp/MessagePack-CSharp> for serialization

## Uses <https://github.com/Azure/DotNetty> and <https://github.com/beetlex-io/BeetleX> for networking

## Uses <https://github.com/akkadotnet/akka.net> for clustering support

## Uses <https://github.com/litegraphdb/litegraph> for auth

## Support multiple databases with a master / primary database

## For data model: Document, CatalogItem and Catalog