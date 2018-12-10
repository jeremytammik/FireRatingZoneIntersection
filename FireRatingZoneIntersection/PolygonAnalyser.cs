using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClipperLib;
using Polygon = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Polygons = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

namespace FireRatingZoneIntersection
{
  class PolygonAnalyser
  {
    //Nearly Entirely the Work of The Builder Coder

    /// <summary>
    /// Consider a Revit length zero 
    /// if is smaller than this.
    /// </summary>
    const double _eps = 1.0e-9;

    /// <summary>
    /// Conversion factor from feet to millimetres.
    /// </summary>
    const double _feet_to_mm = 25.4 * 12;

    /// <summary>
    /// Conversion a given length value 
    /// from feet to millimetres.
    /// </summary>
    static long ConvertFeetToMillimetres( double d )
    {
      if( 0 < d )
      {
        return _eps > d
          ? 0
          : (long) ( _feet_to_mm * d + 0.5 );

      }
      else
      {
        return _eps > -d
          ? 0
          : (long) ( _feet_to_mm * d - 0.5 );

      }
    }

    /// <summary>
    /// Conversion a given length value 
    /// from millimetres to feet.
    /// </summary>
    static double ConvertMillimetresToFeet( long d )
    {
      return d / _feet_to_mm;
    }

    /// <summary>
    /// Return a clipper integer point 
    /// from a Revit model space one.
    /// Do so by dropping the Z coordinate
    /// and converting from imperial feet 
    /// to millimetres.
    /// </summary>
    public IntPoint GetIntPoint( XYZ p )
    {
      return new IntPoint(
        ConvertFeetToMillimetres( p.X ),
        ConvertFeetToMillimetres( p.Y ) );
    }

    /// <summary>
    /// Return a Revit model space point 
    /// from a clipper integer one.
    /// Do so by adding a zero Z coordinate
    /// and converting from millimetres to
    /// imperial feet.
    /// </summary>
    public XYZ GetXyzPoint( IntPoint p )
    {
      return new XYZ(
        ConvertMillimetresToFeet( p.X ),
        ConvertMillimetresToFeet( p.Y ),
        0.0 );
    }

    /// <summary>
    /// Retrieve the boundary loops of the given slab 
    /// top face, which is assumed to be horizontal.
    /// </summary>
    Polygons GetBoundaryLoops( CeilingAndFloor slab )
    {
      int n;
      Polygons polys = null;
      Document doc = slab.Document;
      Autodesk.Revit.ApplicationServices.Application app = doc.Application;

      Options opt = app.Create.NewGeometryOptions();

      GeometryElement geo = slab.get_Geometry( opt );

      foreach( GeometryObject obj in geo )
      {
        Solid solid = obj as Solid;
        if( null != solid )
        {
          foreach( Face face in solid.Faces )
          {
            PlanarFace pf = face as PlanarFace;
            if( null != pf
              && pf.FaceNormal.IsAlmostEqualTo( XYZ.BasisZ ) )
            {
              EdgeArrayArray loops = pf.EdgeLoops;

              n = loops.Size;
              polys = new Polygons( n );

              foreach( EdgeArray loop in loops )
              {
                n = loop.Size;
                Polygon poly = new Polygon( n );

                foreach( Edge edge in loop )
                {
                  IList<XYZ> pts = edge.Tessellate();

                  n = pts.Count;

                  foreach( XYZ p in pts )
                  {
                    poly.Add( GetIntPoint( p ) );
                  }
                }
                polys.Add( poly );
              }
            }
          }
        }
      }
      return polys;
    }

    public List<CurveArray> Execute(
      ExternalCommandData commandData,
      ref string message, Floor boundary, Floor eave )
    {
      List<CurveArray> Results = new List<CurveArray>();

      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
      Document doc = uidoc.Document;

      // Two slabs to intersect.

      CeilingAndFloor[] slab
        = new CeilingAndFloor[2] { eave, boundary };

      // Retrieve the two slabs' boundary loops

      Polygons subj = GetBoundaryLoops( slab[0] );
      Polygons clip = GetBoundaryLoops( slab[1] );

      // Calculate the intersection

      Polygons intersection = new Polygons();

      Clipper c = new Clipper();

      //c.AddPolygons( subj, PolyType.ptSubject );
      //c.AddPolygons( clip, PolyType.ptClip );

      c.AddPaths( subj, PolyType.ptSubject, true );
      c.AddPaths( clip, PolyType.ptClip, true );

      c.Execute( ClipType.ctIntersection, intersection,
        PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd );

      // Check for a valid intersection

      if( 0 < intersection.Count )
      {
        foreach( Polygon poly in intersection )
        {
          CurveArray curves = app.Create.NewCurveArray();
          IntPoint? p0 = null; // first
          IntPoint? p = null; // previous

          foreach( IntPoint q in poly )
          {
            if( null == p0 )
            {
              p0 = q;
            }
            if( null != p )
            {
              curves.Append(
                Line.CreateBound(
                  GetXyzPoint( p.Value ),
                  GetXyzPoint( q ) ) );
            }
            p = q;
          }
          Results.Add( curves );
        }
      }
      return Results;
    }
  }
}
