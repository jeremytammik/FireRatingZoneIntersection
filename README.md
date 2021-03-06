# Fire Rating Zone Intersection

C# .NET Revit add-in sample demonstrating use of 2D Booleans to determine intersections between BIM soffits and fire zoning areas.

Based on Jack Bird's sample material shared and described in
the [Revit API discussion forum](http://forums.autodesk.com/t5/revit-api-forum/bd-p/160) thread
on [automatic creation of void extrusion and retrieval of cut area from element](https://forums.autodesk.com/t5/revit-api-forum/automatic-creation-of-void-extrusion-element-retrieve-cut-area/m-p/8451742).

![Fire Rating Zone Intersection](img/fire_rating_zone_intersection.png)

The test sample model looks like this:

![Test sample model](img/fire_rating_zone_intersection_model.png)

Here is the result of the firdst test run, producing new floor elements to represent the parts of the soffits overlapping the fire zone:

![Test run result](img/fire_rating_zone_intersection_result.png)

For more details, please refer
to [The Building Coder](https://thebuildingcoder.typepad.com) discussion
on [fire rating zone intersection](https://thebuildingcoder.typepad.com/blog/2018/12/fire-rating-zone-intersection.html).


## Authors

- [Jack Bird](https://forums.autodesk.com/t5/user/viewprofilepage/user-id/6830764)
- Jeremy Tammik,
[The Building Coder](http://thebuildingcoder.typepad.com),
[Forge](http://forge.autodesk.com) [Platform](https://developer.autodesk.com) Development,
[ADN](http://www.autodesk.com/adn)
[Open](http://www.autodesk.com/adnopen),
[Autodesk Inc.](http://www.autodesk.com)


## License

This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT).
Please see the [LICENSE](LICENSE) file for full details.
