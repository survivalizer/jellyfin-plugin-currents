#!/usr/bin/env python3
"""Checks that the Jellyfin actions and arguments Currents' MVC filters match on still exist.

Jellyfin names each OpenAPI operation after its controller method (the filters' ActionName). The OpenAPI tag is
NOT the controller class name (ItemsController is tagged "Library", MediaSegmentsController "MediaSegment", and
[Tags] overrides it per action), so the controller column below is documentation only and actions are looked up by
operationId. Actions marked [ApiExplorerSettings(IgnoreApi = true)] (the *Legacy ones) are absent from the document
and cannot be checked here.

Usage: dev/check-contract.py <openapi.json file or http(s) URL>
"""
import json
import sys
import urllib.request

# (controller class, action, argument names). Keep in step with src/Jellyfin.Plugin.Currents/Integration/.
CONTRACT = [
    ("Items", "GetItems", ["searchTerm", "includeItemTypes", "excludeItemTypes", "limit", "startIndex", "parentId", "mediaTypes", "isMissing", "userId"]),
    ("Search", "GetSearchHints", ["searchTerm", "startIndex", "limit", "userId", "includeItemTypes", "excludeItemTypes", "mediaTypes",
                                  "parentId", "isMovie", "isSeries", "isNews", "isKids", "isSports", "includeMedia"]),
    ("MediaInfo", "GetPlaybackInfo", ["itemId", "userId"]),
    ("MediaInfo", "GetPostedPlaybackInfo", ["itemId", "userId", "mediaSourceId", "audioStreamIndex", "subtitleStreamIndex"]),
    ("UserLibrary", "GetItem", ["itemId"]),
    ("Image", "GetItemImage", ["itemId"]),
    ("Image", "GetItemImageByIndex", ["itemId"]),
    ("Image", "GetItemImage2", ["itemId"]),
    ("Subtitle", "GetSubtitle", ["routeItemId", "routeMediaSourceId", "routeIndex", "routeFormat", "itemId", "mediaSourceId", "index"]),
    ("Subtitle", "GetSubtitleWithTicks", ["routeItemId", "routeMediaSourceId", "routeIndex", "routeStartPositionTicks", "itemId", "mediaSourceId", "index"]),
    ("Subtitle", "GetSubtitlePlaylist", ["itemId", "mediaSourceId", "index"]),
    ("MediaSegments", "GetItemSegments", ["itemId"]),
    ("VideoAttachments", "GetAttachment", ["videoId"]),
    ("Playstate", "MarkPlayedItem", ["itemId"]),
    ("Playstate", "MarkUnplayedItem", ["itemId"]),
    ("UserLibrary", "MarkFavoriteItem", ["itemId"]),
    ("UserLibrary", "UnmarkFavoriteItem", ["itemId"]),
]


def load(source):
    if source.startswith(("http://", "https://")):
        with urllib.request.urlopen(source, timeout=30) as response:
            return json.load(response)
    with open(source, encoding="utf-8") as handle:
        return json.load(handle)


def operations(document):
    for path in document.get("paths", {}).values():
        for method, op in path.items():
            if isinstance(op, dict) and "operationId" in op:
                names = {p.get("name") for p in op.get("parameters", [])}
                yield op["operationId"], names


def main():
    if len(sys.argv) != 2:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    found = {}
    for op_id, names in operations(load(sys.argv[1])):
        found.setdefault(op_id, []).append(names)
    problems = []
    for controller, action, arguments in CONTRACT:
        ops = found.get(action, [])
        if not ops:
            problems.append(f"missing action {controller}.{action}")
            continue
        missing = [a for a in arguments if not any(a in names for names in ops)]
        if missing:
            problems.append(f"{controller}.{action}: missing arguments {', '.join(missing)}")
    for line in problems:
        print(line)
    print(f"{len(CONTRACT) - len(problems)}/{len(CONTRACT)} contract entries hold")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
