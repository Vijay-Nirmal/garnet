# Lab Report for JSON support in Garnet

This document presents findings from evaluating various JSON libraries and implementations to support Redis-compatible JSON functionality in Garnet. The key objectives were to:

- Compare major .NET JSON libraries for performance, features and Redis compatibility
- Evaluate memory usage and optimization opportunities 
- Assess approaches for handling larger-than-memory JSON scenarios
- Determine the best implementation strategy for Garnet's JSON module

Redis includes native JSON support via the RedisJSON module. For Garnet to provide full Redis compatibility, we need equivalent JSON capabilities with similar performance characteristics and memory efficiency.

- [Lab Report for JSON support in Garnet](#lab-report-for-json-support-in-garnet)
  - [Summary of the findings](#summary-of-the-findings)
  - [Picking the right Json library](#picking-the-right-json-library)
  - [Details of the libraries tested](#details-of-the-libraries-tested)
    - [JsonPath Feature comparison](#jsonpath-feature-comparison)
    - [JsonPath Performance](#jsonpath-performance)
    - [Conclusion for right library](#conclusion-for-right-library)
  - [Customizing Newtonsoft.Json to support all Redis features](#customizing-newtonsoftjson-to-support-all-redis-features)
    - [Common customizations for all Redis commands](#common-customizations-for-all-redis-commands)
    - [For JSON.GET command](#for-jsonget-command)
    - [For JSON.SET command](#for-jsonset-command)
    - [For JSON.ARRINDEX command](#for-jsonarrindex-command)
    - [For JSON.ARRINSERT command](#for-jsonarrinsert-command)
    - [For JSON.ARRPOP command](#for-jsonarrpop-command)
    - [For JSON.ARRTRIM command](#for-jsonarrtrim-command)
    - [For JSON.DEL command](#for-jsondel-command)
    - [For JSON.MERGE command](#for-jsonmerge-command)
    - [Conclusion for customizing Newtonsoft.Json](#conclusion-for-customizing-newtonsoftjson)
  - [Serialize/Deserialize to support Larger Than Memory (LTM) scenarios](#serializedeserialize-to-support-larger-than-memory-ltm-scenarios)
    - [Steam size comparison after serialization](#steam-size-comparison-after-serialization)
    - [Performance comparison for Serialize/Deserialize](#performance-comparison-for-serializedeserialize)
    - [Code Snippet for Serialize/Deserialize](#code-snippet-for-serializedeserialize)
    - [Conclusion for Serialize/Deserialize](#conclusion-for-serializedeserialize)
  - [Findings Size of JObject](#findings-size-of-jobject)
    - [Code Snippet for Size of JObject](#code-snippet-for-size-of-jobject)
    - [Update Size without calculating the whole object](#update-size-without-calculating-the-whole-object)
    - [Size comparison with Redis](#size-comparison-with-redis)
    - [Conclusion for Size of JObject](#conclusion-for-size-of-jobject)
  - [Code Module vs Custom Module](#code-module-vs-custom-module)
  - [Open Questions](#open-questions)

## Summary of the findings

After extensive testing of JSON libraries and implementations, here are the key findings:

1. Library Selection:
   - Newtonsoft.Json is the best fit for Garnet, offering superior performance and feature support
   - BlushingPenguin.JsonPath is a good alternative but lacks maintenance
   - JsonPath.Net (json-everything) is very bad in terms of performance (which is the current implementation in Garnet)
   - Rest of the analyze is based on Newtonsoft.Json assuming it is the selected library

2. Redis Compatibility:
   - Regex syntax differences exist between Redis and .NET libraries, it will be costly to support Redis syntax unless we have our own implementation
   - Need custom implementations for certain Redis features like MERGE and ARRINSERT
   - Static path detection has to be implemented for Redis JSON.SET command
   - Performance might/will be slower when using JObject compared to Redis's implementation

3. Memory Considerations:
   - JObject size is 5-6x larger than Redis for the same JSON data
   - Memory overhead is a concern for large JSON objects
   - Found ways to find the object size (memory usage) of a JSON Garent Object

4. Larger Than Memory (LTM) scenario:
   - For serialization/deserialization, direct stream operations using JsonTextWriter/JsonTextReader is most efficient
   - String-based serialization performs worst in terms of memory and speed (which is the current implementation in Garnet)
   - BSON format offers good performance for smaller objects but mixed results for larger ones

## Picking the right Json library

Tested the below libraries for Json support for Garnet

- [Newtonsoft.Json v13.0.3](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3)
- [JsonPath.Net (json-everything) v2.0.0](https://www.nuget.org/packages/JsonPath.Net)
- [BlushingPenguin.JsonPath v1.0.6](https://www.nuget.org/packages/BlushingPenguin.JsonPath/1.0.6)
- [Hyperbee.Json v3.0.1](https://www.nuget.org/packages/Hyperbee.Json/3.0.1)
- [JsonCons.JsonPath v1.1.0](https://www.nuget.org/packages/JsonCons.JsonPath/1.1.0)

## Details of the libraries tested

Here are the details of the libraries tested for Json support for Garnet.

| Library Name                   | Total Downloads | Last Updated | License | Source Code                                                                                             |
| ------------------------------ | --------------- | ------------ | ------- | ------------------------------------------------------------------------------------------------------- |
| Newtonsoft.Json                | 5.6B            | 2023-03-08   | MIT     | [JamesNK/Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)                                   |
| JsonPath.Net (json-everything) | 2.2M            | 2024-11-30   | MIT     | [json-everything/JsonPath.Net](https://github.com/json-everything/json-everything)                      |
| BlushingPenguin.JsonPath       | 69.9K           | 2022-01-21   | MIT     | [blushingpenguin/BlushingPenguin.JsonPath](https://github.com/blushingpenguin/BlushingPenguin.JsonPath) |
| Hyperbee.Json                  | 1.7K            | 2024-12-31   | MIT     | [Stillpoint-Software/hyperbee.json](https://github.com/Stillpoint-Software/hyperbee.json)               |
| JsonCons.JsonPath              | 143.4K          | 2021-08-25   | MIT     | [danielaparker/JsonCons.Net](https://github.com/danielaparker/JsonCons.Net)                             |

*Disclaimer*: The above details are based on the information available on NuGet and GitHub as of 2024-12-26.

### JsonPath Feature comparison

Let's compare the features of JsonPath for the above libraries with Redis/Valkey. Below are the features we are going to compare.

| Feature              | Sample JsonPath                                        | Redis/Valkey  | Newtonsoft.Json | JsonPath.Net (json-everything) | BlushingPenguin.JsonPath | Hyperbee.Json | JsonCons.JsonPath |
| -------------------- | ------------------------------------------------------ | ------------- | --------------- | ------------------------------ | ------------------------ | ------------- | ----------------- |
| Recursive descent    | $..*                                                   | Supported     | Supported       | Supported                      | Supported                | Supported     | Supported         |
| Array slicing        | $.store.book[-1:]                                      | Supported     | Supported       | Supported                      | Supported                | Supported     | Supported         |
| Multiple conditions  | $.store.book[?(@.author && @.title)]                   | Supported     | Supported       | Supported                      | Supported                | Supported     | Supported         |
| Range comparison     | $.store.book[?(@.price > 10 && @.price < 20)]          | Supported     | Supported       | Supported                      | Supported                | Supported     | Supported         |
| Numeric comparison   | $.store.book[?(@.price < 10)]                          | Supported     | Supported       | Supported                      | Supported                | Supported     | Supported         |
| Regex                | $.store.book[?(@.author =~ /.*Waugh/)]                 | Supported***  | Supported       | Not Supported                  | Supported                | Not Supported | Supported         |
| Filter by existence  | $.store.book[?(!@.isbn)]                               | Not Supported | Not Supported   | Supported                      | Not Supported            | Supported     | Supported         |
| Function calling     | $.store.book[?(length(@.author) > 5)]                  | Not Supported | Not Supported   | Supported                      | Not Supported            | Not Supported | Supported         |
| Contains (IN clause) | $.store.book[?(@.category in ['fiction','reference'])] | Not Supported | Not Supported   | Not Supported                  | Not Supported            | Supported     | Not Supported     |

*****IMPORTANT:** Regex feature in redis uses different syntax and the .Net libraries. For Redis, the syntax is `$.store.book[?(@.author == ".*Waugh")]` and for .Net libraries, the syntax is `$.store.book[?(@.author =~ /.*Waugh/)]`. None of the .Net libraries support the Redis syntax and Redis does not support the .Net syntax. We might have to customize the library to support the Redis syntax or change the Redis syntax Json Path to .Net syntax or have our own implementation of Json path.

In terms of feature comparison, All libraries support more features than Redis/Valkey. JsonCons.JsonPath supports the most feature. BlushingPenguin.JsonPath is a port of Newtonsoft.Json JsonPath code to System.Text.Json, so both supports the same features.

### JsonPath Performance

Let's compare the performance of JsonPath for the above libraries. Below are the performance comparison for JsonPath for the above libraries with different JsonPath queries.

| Method                           | JsonPath                                               |       Mean |         Ratio | Allocated | Alloc Ratio |
| -------------------------------- | ------------------------------------------------------ | ---------: | ------------: | --------: | ----------: |
| Newtonsoft.Json                  | $..*                                                   |   633.8 ns |      baseline |     320 B |             |
| 'JsonPath.Net (json-everything)' | $..*                                                   | 6,426.0 ns | 10.15x slower |   20344 B | 63.58x more |
| BlushingPenguin.JsonPath         | $..*                                                   | 1,706.4 ns |  2.69x slower |    3344 B | 10.45x more |
| Hyperbee.Json                    | $..*                                                   | 4,325.3 ns |  6.83x slower |    3856 B | 12.05x more |
| JsonCons.JsonPath                | $..*                                                   | 1,161.6 ns |  1.83x slower |    4200 B | 13.12x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $..author                                              |   502.1 ns |      baseline |     336 B |             |
| 'JsonPath.Net (json-everything)' | $..author                                              | 4,624.7 ns |  9.21x slower |   15088 B | 44.90x more |
| BlushingPenguin.JsonPath         | $..author                                              | 1,610.4 ns |  3.21x slower |    3360 B | 10.00x more |
| Hyperbee.Json                    | $..author                                              | 1,933.2 ns |  3.85x slower |    2056 B |  6.12x more |
| JsonCons.JsonPath                | $..author                                              |   796.9 ns |  1.59x slower |    2560 B |  7.62x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $..book[0,1]                                           |   568.0 ns |      baseline |     584 B |             |
| 'JsonPath.Net (json-everything)' | $..book[0,1]                                           | 4,856.8 ns |  8.55x slower |   16136 B | 27.63x more |
| BlushingPenguin.JsonPath         | $..book[0,1]                                           | 1,745.5 ns |  3.07x slower |    3616 B |  6.19x more |
| Hyperbee.Json                    | $..book[0,1]                                           | 2,182.4 ns |  3.84x slower |    2056 B |  3.52x more |
| JsonCons.JsonPath                | $..book[0,1]                                           | 1,156.3 ns |  2.04x slower |    3168 B |  5.42x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store..price                                         |   502.4 ns |      baseline |     472 B |             |
| 'JsonPath.Net (json-everything)' | $.store..price                                         | 4,726.4 ns |  9.41x slower |   15728 B | 33.32x more |
| BlushingPenguin.JsonPath         | $.store..price                                         | 1,326.8 ns |  2.64x slower |    3320 B |  7.03x more |
| Hyperbee.Json                    | $.store..price                                         | 1,703.8 ns |  3.39x slower |    1792 B |  3.80x more |
| JsonCons.JsonPath                | $.store..price                                         |   910.9 ns |  1.81x slower |    2472 B |  5.24x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.*                                              |   148.5 ns |      baseline |     568 B |             |
| 'JsonPath.Net (json-everything)' | $.store.*                                              |   906.6 ns |  6.11x slower |    3440 B |  6.06x more |
| BlushingPenguin.JsonPath         | $.store.*                                              |   128.1 ns |  1.16x faster |     512 B |  1.11x less |
| Hyperbee.Json                    | $.store.*                                              |   652.5 ns |  4.40x slower |    1440 B |  2.54x more |
| JsonCons.JsonPath                | $.store.*                                              |   293.9 ns |  1.98x slower |    1224 B |  2.15x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.bicycle.color                                  |   172.7 ns |      baseline |     632 B |             |
| 'JsonPath.Net (json-everything)' | $.store.bicycle.color                                  | 1,225.3 ns |  7.10x slower |    4440 B |  7.03x more |
| BlushingPenguin.JsonPath         | $.store.bicycle.color                                  |   203.4 ns |  1.18x slower |     688 B |  1.09x more |
| Hyperbee.Json                    | $.store.bicycle.color                                  |   244.0 ns |  1.41x slower |     208 B |  3.04x less |
| JsonCons.JsonPath                | $.store.bicycle.color                                  |   331.8 ns |  1.92x slower |    1184 B |  1.87x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[-1:]                                      |   173.4 ns |      baseline |     656 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[-1:]                                      | 1,114.1 ns |  6.43x slower |    4280 B |  6.52x more |
| BlushingPenguin.JsonPath         | $.store.book[-1:]                                      |   200.5 ns |  1.16x slower |     696 B |  1.06x more |
| Hyperbee.Json                    | $.store.book[-1:]                                      |   601.2 ns |  3.47x slower |    1232 B |  1.88x more |
| JsonCons.JsonPath                | $.store.book[-1:]                                      |   442.1 ns |  2.55x slower |    1480 B |  2.26x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[:2]                                       |   181.4 ns |      baseline |     648 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[:2]                                       | 1,311.9 ns |  7.23x slower |    4648 B |  7.17x more |
| BlushingPenguin.JsonPath         | $.store.book[:2]                                       |   199.9 ns |  1.10x slower |     688 B |  1.06x more |
| Hyperbee.Json                    | $.store.book[:2]                                       |   693.9 ns |  3.83x slower |    1232 B |  1.90x more |
| JsonCons.JsonPath                | $.store.book[:2]                                       |   440.5 ns |  2.43x slower |    1504 B |  2.32x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(!@.isbn)]                               |         NA |             ? |        NA |           ? |
| 'JsonPath.Net (json-everything)' | $.store.book[?(!@.isbn)]                               | 2,482.6 ns |             ? |    8008 B |           ? |
| BlushingPenguin.JsonPath         | $.store.book[?(!@.isbn)]                               |         NA |             ? |        NA |           ? |
| Hyperbee.Json                    | $.store.book[?(!@.isbn)]                               | 1,295.0 ns |             ? |    2120 B |           ? |
| JsonCons.JsonPath                | $.store.book[?(!@.isbn)]                               |   818.8 ns |             ? |    2408 B |           ? |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.author && @.title)]                   |   486.2 ns |      baseline |    1752 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.author && @.title)]                   | 3,442.9 ns |  7.08x slower |   11416 B |  6.52x more |
| BlushingPenguin.JsonPath         | $.store.book[?(@.author && @.title)]                   |   542.6 ns |  1.12x slower |    1896 B |  1.08x more |
| Hyperbee.Json                    | $.store.book[?(@.author && @.title)]                   | 1,796.4 ns |  3.70x slower |    2856 B |  1.63x more |
| JsonCons.JsonPath                | $.store.book[?(@.author && @.title)]                   | 1,175.6 ns |  2.42x slower |    2944 B |  1.68x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.author =~ /.*Waugh/)]                 |   673.3 ns |      baseline |    1456 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.author =~ /.*Waugh/)]                 |         NA |             ? |        NA |           ? |
| BlushingPenguin.JsonPath         | $.store.book[?(@.author =~ /.*Waugh/)]                 | 1,073.9 ns |  1.60x slower |    1912 B |  1.31x more |
| Hyperbee.Json                    | $.store.book[?(@.author =~ /.*Waugh/)]                 |         NA |             ? |        NA |           ? |
| JsonCons.JsonPath                | $.store.book[?(@.author =~ /.*Waugh/)]                 | 2,006.2 ns |  2.98x slower |    5208 B |  3.58x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.category == 'fiction')]               |   408.3 ns |      baseline |    1480 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.category == 'fiction')]               | 3,228.2 ns |  7.91x slower |    9720 B |  6.57x more |
| BlushingPenguin.JsonPath         | $.store.book[?(@.category == 'fiction')]               |   954.7 ns |  2.34x slower |    2120 B |  1.43x more |
| Hyperbee.Json                    | $.store.book[?(@.category == 'fiction')]               | 1,475.3 ns |  3.61x slower |    2584 B |  1.75x more |
| JsonCons.JsonPath                | $.store.book[?(@.category == 'fiction')]               |   983.7 ns |  2.41x slower |    2560 B |  1.73x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.category in ['fiction','reference'])] |         NA |             ? |        NA |           ? |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.category in ['fiction','reference'])] |         NA |             ? |        NA |           ? |
| BlushingPenguin.JsonPath         | $.store.book[?(@.category in ['fiction','reference'])] |         NA |             ? |        NA |           ? |
| Hyperbee.Json                    | $.store.book[?(@.category in ['fiction','reference'])] | 2,383.3 ns |             ? |    4136 B |           ? |
| JsonCons.JsonPath                | $.store.book[?(@.category in ['fiction','reference'])] |         NA |             ? |        NA |           ? |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.price < 10)].title                    |   507.1 ns |      baseline |    1632 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.price < 10)].title                    | 3,598.3 ns |  7.13x slower |   10560 B |  6.47x more |
| BlushingPenguin.JsonPath         | $.store.book[?(@.price < 10)].title                    |   835.9 ns |  1.66x slower |    1912 B |  1.17x more |
| Hyperbee.Json                    | $.store.book[?(@.price < 10)].title                    | 1,713.7 ns |  3.40x slower |    2592 B |  1.59x more |
| JsonCons.JsonPath                | $.store.book[?(@.price < 10)].title                    | 1,325.2 ns |  2.63x slower |    2824 B |  1.73x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.price > 10 && @.price < 20)]          |   623.9 ns |      baseline |    2232 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.price > 10 && @.price < 20)]          | 5,013.7 ns |  8.04x slower |   13784 B |  6.18x more |
| BlushingPenguin.JsonPath         | $.store.book[?(@.price > 10 && @.price < 20)]          | 1,253.7 ns |  2.01x slower |    2680 B |  1.20x more |
| Hyperbee.Json                    | $.store.book[?(@.price > 10 && @.price < 20)]          | 2,510.4 ns |  4.03x slower |    3384 B |  1.52x more |
| JsonCons.JsonPath                | $.store.book[?(@.price > 10 && @.price < 20)]          | 2,048.9 ns |  3.29x slower |    3832 B |  1.72x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(@.price in [8.95, 12.99])]              |         NA |             ? |        NA |           ? |
| 'JsonPath.Net (json-everything)' | $.store.book[?(@.price in [8.95, 12.99])]              |         NA |             ? |        NA |           ? |
| BlushingPenguin.JsonPath         | $.store.book[?(@.price in [8.95, 12.99])]              |         NA |             ? |        NA |           ? |
| Hyperbee.Json                    | $.store.book[?(@.price in [8.95, 12.99])]              | 2,544.0 ns |             ? |    3944 B |           ? |
| JsonCons.JsonPath                | $.store.book[?(@.price in [8.95, 12.99])]              |         NA |             ? |        NA |           ? |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[?(length(@.author) > 5)]                  |         NA |             ? |        NA |           ? |
| 'JsonPath.Net (json-everything)' | $.store.book[?(length(@.author) > 5)]                  | 4,859.1 ns |             ? |   12264 B |           ? |
| BlushingPenguin.JsonPath         | $.store.book[?(length(@.author) > 5)]                  |         NA |             ? |        NA |           ? |
| Hyperbee.Json                    | $.store.book[?(length(@.author) > 5)]                  |         NA |             ? |        NA |           ? |
| JsonCons.JsonPath                | $.store.book[?(length(@.author) > 5)]                  | 1,845.0 ns |             ? |    4600 B |           ? |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[*]                                        |   181.0 ns |      baseline |     632 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[*]                                        | 1,262.3 ns |  6.98x slower |    4624 B |  7.32x more |
| BlushingPenguin.JsonPath         | $.store.book[*]                                        |   180.5 ns |  1.00x faster |     648 B |  1.03x more |
| Hyperbee.Json                    | $.store.book[*]                                        |   651.7 ns |  3.60x slower |    1320 B |  2.09x more |
| JsonCons.JsonPath                | $.store.book[*]                                        |   320.4 ns |  1.77x slower |    1248 B |  1.97x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[*].author                                 |   244.1 ns |      baseline |     784 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[*].author                                 | 2,388.4 ns |  9.79x slower |    7360 B |  9.39x more |
| BlushingPenguin.JsonPath         | $.store.book[*].author                                 |   276.4 ns |  1.13x slower |     816 B |  1.04x more |
| Hyperbee.Json                    | $.store.book[*].author                                 | 1,004.5 ns |  4.12x slower |    1528 B |  1.95x more |
| JsonCons.JsonPath                | $.store.book[*].author                                 |   422.0 ns |  1.73x slower |    1384 B |  1.77x more |
|                                  |                                                        |            |               |           |             |
| Newtonsoft.Json                  | $.store.book[0].title                                  |   224.1 ns |      baseline |     760 B |             |
| 'JsonPath.Net (json-everything)' | $.store.book[0].title                                  | 1,729.0 ns |  7.72x slower |    5792 B |  7.62x more |
| BlushingPenguin.JsonPath         | $.store.book[0].title                                  |   249.4 ns |  1.11x slower |     832 B |  1.09x more |
| Hyperbee.Json                    | $.store.book[0].title                                  |   311.4 ns |  1.39x slower |     208 B |  3.65x less |
| JsonCons.JsonPath                | $.store.book[0].title                                  |   405.3 ns |  1.81x slower |    1264 B |  1.66x more |

Source Code: https://github.com/Vijay-Nirmal/JsonBenchmark

In terms of perforamance, Newtonsoft.Json is the best fit for Garnet. In most cases (except for Recursive descent feature) BlushingPenguin.JsonPath is very close to Newtonsoft.Json.

### Conclusion for right library

From the above exercise, we can conclude that Newtonsoft.Json is the best fit for Garnet. If Newtonsoft.Json is not possible, then BlushingPenguin.JsonPath is the next best fit for Garnet but it looks like is not maintained anymore. We might have to fork (or move the code to Garnet) and maintain it ourselves. There are multiple advantage maintaining our own implementation like we can support Redis syntax of redis commands, we can customize the library to support Redis features, we can optimize the library for Garnet, etc.

For the rest of the analyze, we will consider Newtonsoft.Json as the selected library.

## Customizing Newtonsoft.Json to support all Redis features

### Common customizations for all Redis commands

- Need to add `$.` if the path doesn't start with `$`. For example, if the path is `store.book`, then we need to convert it to `$.store.book`.

### For JSON.GET command

To support the [INDENT indent] [NEWLINE newline] [SPACE space] options in JSON.GET command, we can create a custom JsonTextWriter by inheriting from JsonTextWriter and override the WriteIndentSpace and WriteIndent methods and customize TextWriter.NewLine. Below is the code snippet for the custom JsonTextWriter.

```csharp
public class GarnetJsonTextWriter : JsonTextWriter
{
    private readonly TextWriter _textWriter;
    private string indentString;

    /// <summary>
    /// Sets the indentation string for nested levels.
    /// </summary>
    public string IndentString
    {
        set
        {
            if (value.Length == 1)
            {
                IndentChar = value[0];
                indentString = null;
            }
            else
            {
                indentString = value;
            }
        }
    }

    /// <summary>
    /// Sets the string that's put between a key and a value.
    /// </summary>
    public string SpaceString { get; set; }

    /// <summary>
    /// Sets the string that's printed at the end of each line
    /// </summary>
    public string NewLineString { get => _textWriter.NewLine; set => _textWriter.NewLine = value; }

    public GarnetJsonTextWriter(TextWriter textWriter) : base(textWriter)
    {
        _textWriter = textWriter;
        _textWriter.NewLine = string.Empty;

        Formatting = Formatting.Indented;
        Indentation = 1;
    }

    protected override void WriteIndentSpace()
    {
        if (SpaceString is null)
        {
            base.WriteIndentSpace();
            return;
        }

        _textWriter.Write(SpaceString);
    }

    protected override void WriteIndent()
    {
        if (indentString is null)
        {
            base.WriteIndent();
            return;
        }

        int currentIndentCount = Top * Indentation;

        _textWriter.Write(_textWriter.NewLine);

        for (int i = 0; i < currentIndentCount; i++)
        {
            _textWriter.Write(indentString);
        }
    }
}
```

### For JSON.SET command

- Try finding element using the provided path
- If element found, we need to perform update/replace operation
- If element not found, we need to perform insert operation
  - If the path is static**, then we find the parent*** from the given json path and update the value.
  - If parent is not found, then we should return "(nil)"
  - If the path is not static, then we shoud return "-Err wrong static path"

**Static path is a path that is promised to have at most a single result [Source: json_path.rs](https://github.com/RedisJSON/RedisJSON/blob/12c30d30824af3bede0c191d806711b4cc14d955/json_path/src/json_path.rs#L82). We can verify this by checking if the JsonPath has any wildcards or filters. We can do that by using the below regex.

Regex (Doesn't support unicode): `^\$((\.[a-zA-Z_][a-zA-Z0-9_]*)|(\.{0,1}\[\d+\])|(\.{0,1}\[['"](?:[^'"\[\]]|\\.)*['"]\]))*$`
Regex with Unicode support: `^\$((\.[\p{L}_][\p{L}\p{N}_]*)|(\.{0,1}\[\d+\])|(\.{0,1}\[['"](?:[^'"\[\]]|\\.)*['"]\]))*$`

| Test Case                              | Result     |
| -------------------------------------- | ---------- |
| $                                      | Static     |
| $[0]                                   | Static     |
| $.store                                | Static     |
| $['test']                              | Static     |
| $.store.book[0].title                  | Static     |
| $['store'].["bicycle"]                 | Static     |
| $.store.["bicycle"]                    | Static     |
| $.store..test                          | Not Static |
| $.store.*.test                         | Not Static |
| $.store.book[?(@.price < 10)]          | Not Static |
| $.store.book[?(@.author)]              | Not Static |
| $.store.book[?(@.author =~ ".*Waugh")] | Not Static |

Here is the performance comparison for the above test cases with Regex.IsMatch, Compiled Regex, and Source Generation regex with both Unicode and without Unicode.

| Method                                   | JsonPath                               |        Mean |      Error |   Gen0 | Allocated |
| ---------------------------------------- | -------------------------------------- | ----------: | ---------: | -----: | --------: |
| Regex_Without_Unicode_With_Regex_IsMatch | $                                      |   104.32 ns |   4.165 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $                                      |   111.04 ns |   4.757 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $                                      |    32.85 ns |   1.268 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $                                      |    39.30 ns |  10.068 ns | 0.0034 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $                                      |    32.12 ns |   1.118 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $                                      |    36.97 ns |   6.219 ns | 0.0034 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store                                |   139.00 ns |   5.790 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store                                |   153.17 ns |  11.030 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store                                |    48.20 ns |   2.153 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store                                |    56.10 ns |   1.704 ns | 0.0034 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store                                |    46.05 ns |   2.168 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store                                |    52.96 ns |   6.526 ns | 0.0034 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store..test                          |   566.84 ns |  37.124 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store..test                          |   625.26 ns |  44.428 ns | 0.0029 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store..test                          |   140.34 ns |  30.773 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store..test                          |   146.93 ns |   2.833 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store..test                          |   131.33 ns |   6.180 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store..test                          |   143.40 ns |   8.826 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.["bicycle"]                    |   306.97 ns |  25.473 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.["bicycle"]                    |   319.91 ns |  12.284 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.["bicycle"]                    |    86.61 ns |   2.080 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.["bicycle"]                    |    95.06 ns |  10.405 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.["bicycle"]                    |    84.64 ns |   1.403 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.["bicycle"]                    |    94.76 ns |   8.663 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.*.test                         |   569.32 ns |  37.976 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.*.test                         |   612.90 ns |  19.751 ns | 0.0029 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.*.test                         |   141.18 ns |  31.051 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.*.test                         |   149.24 ns |  16.184 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.*.test                         |   131.05 ns |   9.104 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.*.test                         |   144.27 ns |   3.041 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.book[?(@.author =~ ".*Waugh")] |   971.78 ns |  32.527 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.book[?(@.author =~ ".*Waugh")] | 1,005.44 ns |  98.474 ns | 0.0019 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.book[?(@.author =~ ".*Waugh")] |   229.01 ns |   5.265 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.book[?(@.author =~ ".*Waugh")] |   237.09 ns |  19.196 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.book[?(@.author =~ ".*Waugh")] |   222.52 ns |  10.922 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.book[?(@.author =~ ".*Waugh")] |   241.84 ns |  51.064 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.book[?(@.author)]              | 1,014.49 ns |  29.307 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.book[?(@.author)]              |   997.75 ns | 266.154 ns | 0.0019 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.book[?(@.author)]              |   228.34 ns |  22.528 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.book[?(@.author)]              |   236.99 ns |  23.970 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.book[?(@.author)]              |   217.28 ns |   6.207 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.book[?(@.author)]              |   235.13 ns |  11.970 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.book[?(@.price < 10)]          |   991.03 ns | 115.813 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.book[?(@.price < 10)]          | 1,002.09 ns |  57.532 ns | 0.0019 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.book[?(@.price < 10)]          |   232.23 ns |  17.011 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.book[?(@.price < 10)]          |   239.82 ns |  36.907 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.book[?(@.price < 10)]          |   220.45 ns |   7.761 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.book[?(@.price < 10)]          |   242.88 ns |  36.146 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $.store.book[0].title                  |   290.79 ns |  10.394 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $.store.book[0].title                  |   294.34 ns |  29.074 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $.store.book[0].title                  |    90.99 ns |   1.393 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $.store.book[0].title                  |   101.74 ns |   8.527 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $.store.book[0].title                  |    91.07 ns |  10.582 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $.store.book[0].title                  |    99.98 ns |   7.786 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $['store'].["bicycle"]                 |   424.57 ns |  20.895 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $['store'].["bicycle"]                 |   413.37 ns |   4.488 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $['store'].["bicycle"]                 |   106.77 ns |   4.581 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $['store'].["bicycle"]                 |   113.53 ns |   5.569 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $['store'].["bicycle"]                 |   103.55 ns |  13.064 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $['store'].["bicycle"]                 |   109.81 ns |   3.341 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $['test']                              |   234.89 ns |   7.808 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $['test']                              |   244.58 ns |  17.725 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $['test']                              |    63.32 ns |   8.754 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $['test']                              |    67.79 ns |   3.919 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $['test']                              |    60.61 ns |   2.667 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $['test']                              |    66.30 ns |   1.574 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Regex_IsMatch | $[0]                                   |   156.30 ns |  15.764 ns |      - |         - |
| Regex_With_Unicode_With_Regex_IsMatch    | $[0]                                   |   163.36 ns |  11.571 ns | 0.0033 |      64 B |
| Regex_Without_Unicode_With_Compiled      | $[0]                                   |    46.42 ns |   2.509 ns |      - |         - |
| Regex_With_Unicode_With_Compiled         | $[0]                                   |    53.32 ns |   4.047 ns | 0.0034 |      64 B |
| Regex_Without_Unicode_With_Source_Gen    | $[0]                                   |    43.72 ns |   2.506 ns |      - |         - |
| Regex_With_Unicode_With_Source_Gen       | $[0]                                   |    49.39 ns |   0.920 ns | 0.0034 |      64 B |

From the above performance comparison, we can conclude that Source Generation regex is the performant one but very close to compiled regex. We can use the Source Generation regex for Garnet. We should look further if we can improve the performance more.

***To find the parent, we can use the below regex to find the parent path then use that to find the element.

```csharp
static string GetParentPath(string path)
{
    var pathSpan = path.AsSpan();
    // Removed the last character from the path to remove the trailing ']' or '.', it should affect the result even if it doesn't have
    var lastIndex = pathSpan[..^1].LastIndexOfAny('.', ']');

    return path.Substring(0, lastIndex);
}
```

Property name need to add a new child. Below is the code snippet to find the property name from the given path. We can combine this with the above code to find the parent path to avoid multiple LastIndexOfAny operations.

```csharp
static string GetPropertyName(string path)
{
    var pathSpan = path.AsSpan();
    // Removed the last character from the path to remove the trailing ']' or '.', it should affect the result even if it doesn't have
    var lastIndex = pathSpan[..^1].LastIndexOfAny('.', ']');

    var propertyNameSpan = pathSpan[(lastIndex + 1)..];
    if (propertyNameSpan[0] is '[')
    {
        propertyNameSpan = propertyNameSpan[1..^1];
    }
    if (propertyNameSpan[0] is '"' or '\'')
    {
        propertyNameSpan = propertyNameSpan[1..^1];
    }
    return propertyNameSpan.ToString();
}
```

### For JSON.ARRINDEX command

We can't use the built-in IndexOf method as it doesn't perform deep equals, also it doesn't account for START and STOP options supported by Redis. We can use the below code snippet to find the index of the element in the array.

```csharp
static int IndexOf(this JArray array, JToken value, int start, int stop)
{
    for (int i = start; i < stop; i++)
    {
        if (JToken.DeepEquals(array[i], value))
        {
            return i;
        }
    }
    return -1;
}
```

### For JSON.ARRINSERT command

There is no InsertRange method in JArray. There is no way to customize the JArray to support InsertRange. We have to call insert multiple times to insert multiple elements.

### For JSON.ARRPOP command

```csharp
static JToken Pop(JArray array, int index)
{
    var item = array[index];
    array.RemoveAt(index);
    return item;
}
```

### For JSON.ARRTRIM command

There is no RemoveRange method in JArray. There is no way to customize the JArray to support RemoveRange. We have to call insert multiple times to insert multiple elements.

```csharp
static int TrimArray(JArray array, int start, int stop)
{
    if (array == null || array.Count == 0)
        return 0;

    // Convert negative indices to positive
    if (start < 0)
        start = array.Count + start;
    if (stop < 0)
        stop = array.Count + stop;

    // Validate ranges
    start = Math.Max(0, start);
    stop = Math.Min(array.Count - 1, stop);

    if (start > stop)
    {
        array.Clear();
        return 0;
    }

    // Remove elements from end to start to avoid index shifting
    for (int i = array.Count - 1; i > stop; i--)
    {
        array.RemoveAt(i);
    }

    // Remove elements from start backwards
    for (int i = start - 1; i >= 0; i--)
    {
        array.RemoveAt(i);
    }

    return array.Count;
}
```

### For JSON.DEL command

We can use the below code snippet to delete the element after finding the element using the given path. Make sure to store the elements to be deleted in a list and delete them after the iteration to avoid index shifting or null reference exception.

```csharp
static void Delete(JToken token)
{
    if (token.Parent is JProperty parPro && parPro.Parent is JObject parObj)
    {
        parObj.Remove(parPro.Name);
    }
    else
    {
        token.Remove();
    }
}
```

### For JSON.MERGE command

We have to customize the Merge method to support deletion of the element if the value is null as redis does that. Below is the code snippet for the Merge method.

```csharp
static void Merge(JContainer target, JToken content)
{
    var settings = new JsonMergeSettings
    {
        MergeArrayHandling = MergeArrayHandling.Replace,
        MergeNullValueHandling = MergeNullValueHandling.Merge,
        PropertyNameComparison = StringComparison.Ordinal,
    };
    if (target is JObject targetObj)
    {
        MergeObject(targetObj, content, settings);
    }
    else if (target is JArray targetArr)
    {
        MergeArray(targetArr, content, settings);
    }
    else
    {
        target.Merge(content);
    }
}


static void Merge(JObject target, JToken content, JsonMergeSettings settings)
{
    if (!(content is JObject o))
    {
        return;
    }

    foreach (KeyValuePair<string, JToken> contentItem in o)
    {
        JProperty existingProperty = target.Property(contentItem.Key, settings?.PropertyNameComparison ?? StringComparison.Ordinal);

        if (existingProperty == null)
        {
            target.Add(contentItem.Key, contentItem.Value);
        }
        else if (contentItem.Value != null)
        {
            if (!(existingProperty.Value is JContainer existingContainer) || existingContainer.Type != contentItem.Value.Type)
            {
                if (IsNull(contentItem.Value))
                {
                    target.Remove(existingProperty.Name);
                }
                else
                {
                    existingProperty.Value = contentItem.Value;
                }
            }
            else
            {
                if (existingContainer is JObject innerSourceObj)
                {
                    Merge(innerSourceObj, contentItem.Value, settings);
                }
                if (existingContainer is JArray innerSourceArr)
                {
                    MergeArray(innerSourceArr, contentItem.Value, settings);
                }
                else
                {
                    existingContainer.Merge(contentItem.Value, settings);
                }
            }
        }
    }

    static bool IsNull(JToken token)
    {
        if (token.Type == JTokenType.Null)
        {
            return true;
        }

        if (token is JValue v && v.Value == null)
        {
            return true;
        }

        return false;
    }
}

static void MergeArray(JArray target, JToken content, JsonMergeSettings settings)
{
    int i = 0;
    foreach (var targetItem in content)
    {
        if (i < target.Count)
        {
            JToken sourceItem = target[i];

            if (sourceItem is JObject existingObj)
            {
                Merge(existingObj, targetItem, settings);
            }
            else if (sourceItem is JArray existingArray)
            {
                MergeArray(existingArray, targetItem, settings);
            }
            else if (sourceItem is JContainer existingContainer)
            {
                existingContainer.Merge(targetItem, settings);
            }
            else
            {
                if (targetItem != null)
                {
                    if (targetItem.Type != JTokenType.Null)
                    {
                        target[i] = targetItem;
                    }
                }
            }
        }
        else
        {
            target.Add(targetItem);
        }

        i++;
    }
}
```

### Conclusion for customizing Newtonsoft.Json

We can conclude that we can customize Newtonsoft.Json to support all Redis features. But we have to be careful with the performance as I don't believe Newtonsoft.Json is optimized for Redis features. We might have to look for writing our own Json parser atleast for certain functionalities to achieve equalent or better performance than Redis.

## Serialize/Deserialize to support Larger Than Memory (LTM) scenarios

Lets test 3 approaches to serialize/deserialize a JObject to a steam. This is need when we have to support Larger Than Memory (LTM) scenarios with Json objects.

1. Approach 1: Serialize JObject to and from a stream directly using JsonTextWriter and JsonTextReader
2. Approach 2: Serialize JObject to and from a stream using String (TODO: Add a json options to remove whitespace and test again)
3. Approach 3: Serialize JObject to and from a stream using Bson format (Newtonsoft.Json.Bson)

### Steam size comparison after serialization

| Approach   | Json String Lenght | Size of Stream |
| ---------- | ------------------ | -------------- |
| Approach 1 | 59                 | 42             |
| Approach 2 | 59                 | 59             |
| Approach 3 | 59                 | 52             |
|            |                    |                |
| Approach 1 | 415                | 240            |
| Approach 2 | 415                | 415            |
| Approach 3 | 415                | 273            |
|            |                    |                |
| Approach 1 | 926                | 454            |
| Approach 2 | 926                | 926            |
| Approach 3 | 926                | 526            |
|            |                    |                |
| Approach 1 | 3165               | 1824           |
| Approach 2 | 3165               | 3165           |
| Approach 3 | 3165               | 2038           |

In terms of stream size,
- **Approach 1 is the best fit** for Garnet, it has the smallest stream size.
- Approach 2 is bad for large string, it has the same size as the string (it adds whitespace and other characters to the stream).
- Approach 3 is also a good fit for Garnet.

### Performance comparison for Serialize/Deserialize

Let's compare the performance of Serialize/Deserialize for the above 3 approaches. Below are the performance comparison for Serialize/Deserialize for the above 3 approaches with different JsonString lengths.

| Method                  | JsonString |        Mean |     Error |   Gen0 |   Gen1 | Allocated |
| ----------------------- | ---------- | ----------: | --------: | -----: | -----: | --------: |
| Approach 1: Serialize   | 59         |    314.5 ns |   1.12 ns | 0.1373 | 0.0010 |    2592 B |
| Approach 1: Deserialize | 59         |    961.5 ns |   3.43 ns | 0.3700 | 0.0076 |    6976 B |
| Approach 2: Serialize   | 59         |    383.0 ns |   2.01 ns | 0.1688 | 0.0014 |    3184 B |
| Approach 2: Deserialize | 59         |    990.1 ns |  14.55 ns | 0.3910 | 0.0076 |    7376 B |
| Approach 3: Serialize   | 59         |    435.0 ns |   8.51 ns | 0.0677 |      - |    1280 B |
| Approach 3: Deserialize | 59         |    759.1 ns |   6.16 ns | 0.1230 | 0.0010 |    2320 B |
|                         |            |             |           |        |        |           |
| Approach 1: Serialize   | 415        |  1,042.8 ns |   8.22 ns | 0.1507 |      - |    2840 B |
| Approach 1: Deserialize | 415        |  3,771.9 ns |  71.27 ns | 0.6523 | 0.0153 |   12296 B |
| Approach 2: Serialize   | 415        |  1,512.2 ns |   9.81 ns | 0.4425 | 0.0095 |    8360 B |
| Approach 2: Deserialize | 415        |  3,884.7 ns |  12.77 ns | 0.7629 | 0.0229 |   14448 B |
| Approach 3: Serialize   | 415        |  1,798.0 ns |  13.06 ns | 0.1926 | 0.0019 |    3640 B |
| Approach 3: Deserialize | 415        |  3,275.0 ns |  64.31 ns | 0.4082 | 0.0076 |    7752 B |
|                         |            |             |           |        |        |           |
| Approach 1: Serialize   | 926        |  1,585.8 ns |  27.64 ns | 0.3204 | 0.0057 |    6064 B |
| Approach 1: Deserialize | 926        |  6,363.3 ns |  45.76 ns | 0.9842 | 0.0458 |   18608 B |
| Approach 2: Serialize   | 926        |  2,119.1 ns |  14.32 ns | 0.5608 | 0.0114 |   10600 B |
| Approach 2: Deserialize | 926        |  7,055.0 ns |  38.18 ns | 1.2360 | 0.0687 |   23312 B |
| Approach 3: Serialize   | 926        |  3,359.8 ns |  12.83 ns | 0.3586 | 0.0038 |    6784 B |
| Approach 3: Deserialize | 926        |  6,728.9 ns |  29.18 ns | 0.7706 | 0.0305 |   14512 B |
|                         |            |             |           |        |        |           |
| Approach 1: Serialize   | 3165       |  5,135.5 ns |  26.97 ns | 0.3433 |      - |    6496 B |
| Approach 1: Deserialize | 3165       | 21,485.6 ns | 124.60 ns | 2.4109 | 0.3357 |   45936 B |
| Approach 2: Serialize   | 3165       |  7,097.6 ns |  33.08 ns | 1.1520 | 0.0381 |   21792 B |
| Approach 2: Deserialize | 3165       | 23,657.1 ns | 188.01 ns | 3.4180 | 0.4272 |   64808 B |
| Approach 3: Serialize   | 3165       | 10,605.0 ns |  65.23 ns | 0.9918 | 0.0458 |   18680 B |
| Approach 3: Deserialize | 3165       | 21,521.5 ns | 309.52 ns | 2.2278 | 0.2747 |   42464 B |

In terms of performance, there is no clear winner. We can conclude that, approach 2 is worst.
- Approach 1 has the best performance for Serialization, but for deserialization its a mixed bag with approach 3.
- Approach 2 is the worst for both Serialization and Deserialization.
- Approach 3 is the best performar for smaller json objects but for larger json objects, it's a mixed bag with approach 1.

### Code Snippet for Serialize/Deserialize

**Serialize JObject to and from a stream directly using JsonTextWriter and JsonTextReader**

```csharp
// Serialize JObject to a stream
using var writer = new StreamWriter(_outputStream, leaveOpen: true);
using var jsonWriter = new JsonTextWriter(writer);
_jsonSerializer.Serialize(jsonWriter, _newtonsoftJson);
jsonWriter.Flush();

// Deserialize JObject from a stream
using var reader = new StreamReader(_inputStream, leaveOpen: true);
using var jsonReader = new JsonTextReader(reader);
var result = _jsonSerializer.Deserialize<JToken>(jsonReader);
```

**Serialize JObject to and from a stream using String**

```csharp
// Serialize JObject to a stream
using var writer = new StreamWriter(_outputStream, leaveOpen: true);
writer.Write(_newtonsoftJson.ToString());

// Deserialize JObject from a stream
using var reader = new StreamReader(_inputStream, leaveOpen: true);
var result = JToken.Parse(reader.ReadToEnd());
```

**Serialize JObject to and from a stream using Bson format (Newtonsoft.Json.Bson)**

```csharp
// Serialize JObject to a stream
using var writer = new BsonDataWriter(_binaryWriter);
_jsonSerializer.Serialize(writer, _newtonsoftJson);

// Deserialize JObject from a stream
using var reader = new BsonDataReader(_binaryReader);
var result = _jsonSerializer.Deserialize<JToken>(reader);
```

### Conclusion for Serialize/Deserialize

From the above exercise, we can conclude that **Approach 1 is the best fit** for Garnet. If Approach 1 is not possible, then Approach 3 is the next best fit for Garnet. **Haven't tested any of the approaches with Garnet yet.**

## Findings Size of JObject

In Garnet, we have to update the Size property of GarnetObject based on the size of JToken. Below are the findings for the size of JToken.

| Senario                         | Sample Json                      | Size of JObject (Bytes) | Explanation                                                                                                                                                       |
| ------------------------------- | -------------------------------- | ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Empty JObject overhead          | {}                               | 176                     | 176                                                                                                                                                               |
| Empty JArray overhead           | []                               | 128                     | 128                                                                                                                                                               |
| Empty JValue overhead           | null                             | 64                      | 64                                                                                                                                                                |
| Empty JProperty overhead        | "a": null                        | 192                     | 128 (JProperty Key overhead) + 64 (JValue overhead, even null is a value)                                                                                         |
| JProperty with JArray           | "a": []                          | 256                     | 128 (JProperty Key overhead) + 128 (Empty JArray overhead)                                                                                                        |
| JProperty with JObject          | "a": {}                          | 304                     | 128 (JProperty Key overhead) + 176 (Empty JObject overhead)                                                                                                       |
| JObject with 1 key              | { "a": null }                    | 640                     | 176 (Empty JObject overhead) + 192 (Empty JProperty overhead) + 272 (Object Initilizion overhead)                                                                 |
| JObject with other keys         | { "a": null, "b": null }         | 832                     | 176 (Empty JObject overhead) + (192 (Empty JProperty overhead) * 2) + 272 (Object Initilizion overhead)                                                           |
| JArray with 1 item              | [null]                           | 272                     | 128 (Empty JArray overhead) + 64 (Empty JValue overhead) + 56 (Array Initilizion overhead)                                                                        |
| JArray with other items         | [null, null]                     | 312                     | 128 (Empty JArray overhead) + (64 (Empty JValue overhead) * 2) + 56 (Array Initilizion overhead)                                                                  |
| JArray with array               | [[]]                             | 312                     | (128 (Empty JArray overhead) * 2) + 56 (Initilizion overhead)                                                                                                     |
| JArray with 2 array             | [[], []]                         | 440                     | (128 (Empty JArray overhead) * 3) + 56 (Initilizion overhead)                                                                                                     |
| JArray with JObject             | [{}]                             | 360                     | 128 (Empty JArray overhead) + 176 (Empty JObject overhead) + 56 (Array Initilizion overhead)                                                                      |
| JArray with JObject             | [{ "a": null }]                  | 824                     | 128 (Empty JArray overhead) + 176 (Empty JObject overhead) + 56 (Array Initilizion overhead) + 272 (Object Initilizion overhead) + 192 (Empty JProperty overhead) |
| JObject with JArray             | { "a": [] }                      | 704                     | 176 (Empty JObject overhead) + 128 (JProperty Key overhead) + 128 (Empty JArray overhead) + 272 (Object Initilizion overhead)                                     |
| JObject with JObject            | { "a": {} }                      | 752                     | 176 (Empty JObject overhead) + 128 (JProperty Key overhead) + 128 (Empty JArray overhead) + 272 (Object Initilizion overhead)                                     |
| JObject with JObject and JArray | { "a": ["x"], "b": { "y": "z"} } | 1592                    | (176 (Empty JObject) * 2) + (272 (Object Initilizion) * 2) + 128 (Empty JArray) + 56 (Array Initilizion) + (128 (JProperty Key) * 3) + (64 (JValue overhead) * 2) |

**Note:** Size of JValue includes 8 bytes for the actual value (null, true, false, number, char, pointer, etc) and 56 bytes for the overhead because JValue stored the value in a boxed object.

Below is the code snippet to calculate the size of an object in C#.

```csharp
var before = GC.GetTotalMemory(true);
var obj = new JObject(); // Create the object to find the size. Don't use Parse or Deserialize method as it will create the object in a different way with more overhead. 
var after = GC.GetTotalMemory(true);
Console.WriteLine($"Object Size: {after - before - 24}"); // -24 is managed object overhead
```

### Code Snippet for Size of JObject

Below is the code snippet to calculate the size of JToken.

```csharp
public static class JsonSizeCalculator
{
    private const int JOBJECT_OVERHEAD = 176;
    private const int JARRAY_OVERHEAD = 128;
    private const int JVALUE_OVERHEAD = 64;
    private const int JPROPERTY_KEY_OVERHEAD = 128;
    private const int OBJECT_INITIALIZATION_OVERHEAD = 272;
    private const int ARRAY_INITIALIZATION_OVERHEAD = 56;

    public static int CalculateSize(JToken token)
    {
        if (token == null)
            return 0;

        switch (token)
        {
            case JObject obj:
                int objSize = JOBJECT_OVERHEAD;
                if (obj.Count > 0)
                {
                    objSize += OBJECT_INITIALIZATION_OVERHEAD;
                    foreach (var prop in obj.Properties())
                    {
                        var propSize = JPROPERTY_KEY_OVERHEAD;
                        if (prop.Name.Length > 0)
                        {
                            propSize += prop.Name.Length; // Need to do * 2 for unicode characters
                        }
                        objSize += propSize; // Property key overhead
                        objSize += CalculateSize(prop.Value); // Property value
                    }
                }
                return objSize;

            case JArray arr:
                int arrSize = JARRAY_OVERHEAD;
                if (arr.Count > 0)
                {
                    arrSize += ARRAY_INITIALIZATION_OVERHEAD;
                    foreach (var item in arr)
                    {
                        arrSize += CalculateSize(item);
                    }
                }
                return arrSize;

            case JValue val:
                var valSize = JVALUE_OVERHEAD;
                if (val.Value is string valStr)
                {
                    valSize += valStr.Length; // Need to do * 2 for unicode characters
                }
                return valSize;

            default:
                return 0;
        }
    }
}
```

TODO: Benchmark the above code snippet to find the performance

### Update Size without calculating the whole object

To update the size on the fly without calculating the whole object, we can calculate the size of the old value and new value using the above code and update the size accordingly

### Size comparison with Redis

Below is the size comparison of the same object in Redis and JObject.

| Sample Json                      | Size of JObject (Bytes) | Size of Redis (Bytes) | JObject Size - Redis Size(Bytes) | JObject Size / Redis Size (Times) |
| -------------------------------- | ----------------------- | --------------------- | -------------------------------- | --------------------------------- |
| {}                               | 176                     | 8                     | 168                              | 22x                               |
| []                               | 128                     | 8                     | 120                              | 16x                               |
| { "a": null }                    | 641                     | 129                   | 512                              | ~5x                               |
| { "a": null, "b": null }         | 833                     | 138                   | 695                              | ~6x                               |
| { "a": ["x"], "b": { "y": "z"} } | 1597                    | 317                   | 1280                             | ~5x                               |
| Actual small sized Json          | 4880                    | 936                   | 3944                             | ~5.2x                             |
| Actual medium sized Json         | 9485                    | 1893                  | 7592                             | ~5x                               |
| Actual large sized Json          | 31456                   | 6312                  | 25740                            | ~5.5x                             |

From the above comparison, its safe to conclude that JObject size is 5x to 6x larger than Redis size. Which mean if we use **JObject to store the data, we need 5x to 6x more memory than Redis**.

### Conclusion for Size of JObject

We can conclude that we can calculate the size of JToken based on the type of JToken. We can use the above code snippet to calculate and update the size of JToken. But when comparing with Redis, JObject size is 5x to 6x larger than Redis size. We have to be careful with the memory usage when using JObject or we might have to look for writing our own Json parser with Redis specfic feature.

## Code Module vs Custom Module

In Garnet, we have to decide whether to use the code module or custom module to support the Json operations. Below are the findings for the code module and custom module.

TODO: Add the findings

## Open Questions

1. Which library to use for JsonPath/Json Object implementation?
2. Should we consider writing our own JSON parser optimized for Redis operations?
3. Can we add Json module as part of core instead of custom module? (Redis 8.0 will be adding Json module as part of core)
4. Should we modify Redis syntax to match .NET libraries? As well as should we allow .Net syntax which is not valid in Redis?
5. Should I continue having fun in create this document? 😄