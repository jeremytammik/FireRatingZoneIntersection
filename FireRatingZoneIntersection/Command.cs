using System;
using System.Collections.Generic;
using System.ComponentModel;
//using System.Data;
//using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
//using System.Windows.Forms;
//using AddPanel.Classes;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Analysis;
using System.Diagnostics;
using ClipperLib;
using Polygon = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Polygons = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;
//using Microsoft.JScript.Vsa;

namespace FireRatingZoneIntersection
{
  class Example
  {
    private void SubDivideSoffits_CreateFireRatedLayers( ExternalCommandData revit, Document doc )
    {
      try
      {
        #region Get Soffits
        List<Element> Soffits = new FilteredElementCollector( doc ).OfCategory( BuiltInCategory.OST_Floors ).ToElements().Where( m => !( m is ElementType ) ).ToList();

        #endregion

        //Subdivide
        foreach( Element Soffit in Soffits.Where( m => m.Name.ToLower().Contains( "eave" ) ) )
        {
          #region Get Soffit Geometry
          Options ops = new Options();
          ops.DetailLevel = ViewDetailLevel.Fine;
          ops.IncludeNonVisibleObjects = true;
          GeometryElement Geo = Soffit.get_Geometry( ops );

          #endregion

          foreach( var item in Geo )
          {
            if( item is Solid )
            {
              #region Get one of the Main Faces, it doesn't really matter if it is top or bottom
              Solid GSol = item as Solid;
              List<Face> Fs = new List<Face>();
              foreach( Face f in GSol.Faces )
              {
                Fs.Add( f );
              }
              Face F = Fs.Where( m => m.Area == Fs.Max( a => a.Area ) ).First();
              #endregion

              #region Triangulate the Face with max detail
              Mesh M = F.Triangulate( 1 );
              #endregion

              #region Create Variables for: the curves that will define the new Soffits, List of Custom Triangle Class, List of Custom Pair of Triangle Class
              List<List<Curve>> LLC = new List<List<Curve>>();
              List<Triangle> Triangles = new List<Triangle>();
              List<TrianglePair> TPairs = new List<TrianglePair>();

              #endregion

              #region Loop Through Triangles & Add Them to the list of My Triangle Class
              for( int i = 0; i < M.NumTriangles; i++ )
              {
                List<Curve> LC = new List<Curve>();

                #region Make List of Curves From Triangle
                MeshTriangle MT = M.get_Triangle( i );
                List<Curve> Curves = new List<Curve>();
                Curve C = Line.CreateBound( MT.get_Vertex( 0 ), MT.get_Vertex( 1 ) ) as Curve;
                Curves.Add( C );
                C = Line.CreateBound( MT.get_Vertex( 1 ), MT.get_Vertex( 2 ) ) as Curve;
                Curves.Add( C );
                C = Line.CreateBound( MT.get_Vertex( 2 ), MT.get_Vertex( 0 ) ) as Curve;
                Curves.Add( C );
                #endregion

                Triangle T = new Triangle();
                T.Sides = new List<Curve>();
                T.Sides = Curves;

                T.Vertices = new List<XYZ>();
                T.Vertices.Add( MT.get_Vertex( 0 ) );
                T.Vertices.Add( MT.get_Vertex( 1 ) );
                T.Vertices.Add( MT.get_Vertex( 2 ) );
                Triangles.Add( T );
              }
              #endregion

              #region Loop Through Triangles And Create Trapezoid Pairs To Catch The Segments, Getting Rid of The Shared sides
              bool GO = true;
              do
              {
                Triangle TKeeper1 = new Triangle();
                Triangle TKeeper2 = new Triangle();

                foreach( Triangle T in Triangles )
                {
                  TKeeper1 = new Triangle();
                  foreach( Triangle T2 in Triangles )
                  {
                    TKeeper2 = new Triangle();
                    if( T != T2 )
                    {
                      if( FindCurvesFacing( T, T2 ) != null )
                      {
                        if( FindCurvesFacing( T, T2 )[0].Length == T.Sides.Min( c => c.Length ) ||
                          FindCurvesFacing( T, T2 )[1].Length == T2.Sides.Min( c => c.Length ) )
                        {
                          continue;
                        }
                        Curve[] Cs = FindCurvesFacing( T, T2 );
                        T.Sides.Remove( Cs[0] );
                        T2.Sides.Remove( Cs[1] );
                        if( T.Sides.Count() == 2 && T2.Sides.Count() == 2 )
                        {
                          TKeeper1 = T;
                          TKeeper2 = T2;
                          goto ADDANDGOROUND;
                        }
                      }
                    }
                  }
                }
                GO = false;
              ADDANDGOROUND:
                if( GO )
                {
                  Triangles.Remove( TKeeper1 );
                  Triangles.Remove( TKeeper2 );
                  TrianglePair TP = new TrianglePair();
                  TP.T1 = TKeeper1;
                  TP.T2 = TKeeper2;
                  TPairs.Add( TP );
                }
              } while( GO );

              #endregion

              #region Create Curve Loops From Triangle Pairs
              foreach( TrianglePair TPair in TPairs )
              {
                List<Curve> Cs = new List<Curve>();

                Cs.AddRange( TPair.T1.Sides );
                Cs.AddRange( TPair.T2.Sides );

                LLC.Add( Cs );
              }
              #endregion

              double Offset = Convert.ToDouble( Soffit.LookupParameter( "Height Offset From Level" ).AsValueString() );
              FloorType FT = ( Soffit as Floor ).FloorType;
              Level Lvl = doc.GetElement( ( Soffit as Floor ).LevelId ) as Level;

              #region Delete Old Soffit If All Went Well
              using( Transaction T = new Transaction( doc, "Delete Soffit" ) )
              {
                T.Start();
                doc.Delete( Soffit.Id );
                T.Commit();
              }
              #endregion

              #region Sort The Lists of Curves and Create The New Segments
              foreach( List<Curve> LC in LLC )
              {
                List<Curve> LCSorted = new List<Curve>();
                try
                {
                  LCSorted = SortCurvesContiguous( LC, false );
                }

                #region Exception Details if Curves Could not be sorted
                catch( Exception EXC )
                {
                  string exmsge = EXC.Message;
                }

                #endregion

                CurveArray CA = new CurveArray();
                foreach( Curve C in LCSorted )
                {
                  CA.Append( C );
                }

                using( Transaction T = new Transaction( doc, "Make Segment" ) )
                {
                  T.Start();
                  Floor newFloor = doc.Create.NewFloor( CA, FT, Lvl, false );
                  newFloor.LookupParameter( "Height Offset From Level" ).SetValueString( Offset.ToString() );
                  T.Commit();
                }

              }
              #endregion
            }
          }
        }
        //refresh collection
        Soffits = new FilteredElementCollector( doc ).OfCategory( BuiltInCategory.OST_Floors ).ToElements().Where( m => !( m is ElementType ) ).ToList();
        //test soffits for needing fire rating
        foreach( Element Soffit in Soffits.Where( m => m.Name.ToLower().Contains( "eave" ) ) )
        {
          #region Get Soffit Geometry
          Options ops = new Options();
          ops.DetailLevel = ViewDetailLevel.Fine;
          ops.IncludeNonVisibleObjects = true;
          GeometryElement Geo = Soffit.get_Geometry( ops );

          #endregion

          foreach( var item in Geo )
          {
            if( item is Solid )
            {
              #region Find boundary Void Element
              List<Element> MaybeBoundary = new FilteredElementCollector( doc ).OfCategory( BuiltInCategory.OST_Floors ).ToElements().Where( m => !( m is ElementType ) ).ToList();
              Element BoundryElement = MaybeBoundary.Where( m => !( m is FloorType ) && m.Name == "Boundary" ).First();

              #endregion

              #region Get Intersection of Boundary and eave
              PolygonAnyliser com = new PolygonAnyliser();
              string RefString = "";
              List<CurveArray> CArray = com.Execute( revit, ref RefString, BoundryElement as Floor, Soffit as Floor );

              Level L = doc.GetElement( Soffit.LevelId ) as Level;

              #endregion

              foreach( CurveArray CA in CArray )
              {
                #region Sort The Curves 
                IList<Curve> CAL = new List<Curve>();
                foreach( Curve C in CA )
                {
                  CAL.Add( C );
                }

                List<Curve> Curves = SortCurvesContiguous( CAL, false );
                List<XYZ> NewCurveEnds = new List<XYZ>();

                #endregion

                #region Close the loop if nesesary
                CurveLoop CL = new CurveLoop();
                foreach( Curve curv in Curves )
                {
                  CL.Append( curv );
                }
                if( CL.IsOpen() )
                {
                  Curves.Add( Line.CreateBound( CL.First().GetEndPoint( 0 ), CL.Last().GetEndPoint( 1 ) ) as Curve );
                }
                #endregion

                #region Recreate a Curve Array
                Curves = SortCurvesContiguous( Curves, false );

                CurveArray CA2 = new CurveArray();

                int i = 0;
                foreach( Curve c in Curves )
                {
                  CA2.Insert( c, i );
                  i += 1;
                }

                #endregion

                #region Create The New Fire Rated Layer element
                FloorType ft = new FilteredElementCollector( doc ).WhereElementIsElementType().OfCategory( BuiltInCategory.OST_Floors ).ToElements().Where( m => m.Name == "Fire Rated Layer" ).First() as FloorType;
                Transaction T = new Transaction( doc, "Fire Rated Layer Creation" );
                try
                {
                  T.Start();
                  Floor F = doc.Create.NewFloor( CA2, ft, L, false );
                  string s = Soffit.LookupParameter( "Height Offset From Level" ).AsValueString();
                  double si = Convert.ToDouble( s );
                  si = si + ( Convert.ToDouble( Soffit.LookupParameter( "Thickness" ).AsValueString() ) );
                  F.LookupParameter( "Height Offset From Level" ).SetValueString( si.ToString() );
                  T.Commit();
                }
                catch( Exception EX )
                {
                  T.RollBack();
                  string EXmsg = EX.Message;
                }

                #endregion
              }
            }
          }
        }
      }
      catch( Exception ex )
      {
        string mesg = ex.Message;
      }
    }

    //T0 will get it's curve that faces T1 as index 0 and T1 get it's curve that faces T0 as index 1
    Curve[] FindCurvesFacing( Triangle T0, Triangle T1 )
    {
      Curve[] FacingCurves = null;
      XYZ outerVert0 = new XYZ();
      XYZ outerVert1 = new XYZ();
      int workedforvertice = 0;
      int workedforLine = 0;
      foreach( XYZ T0vertice in T0.Vertices )
      {
        foreach( XYZ T1Vertice in T1.Vertices )
        {
          if( T0vertice.IsAlmostEqualTo( T1Vertice ) )
          {
            continue;
          }
          if( T0.Sides.Where( m => m.GetEndPoint( 0 ).IsAlmostEqualTo( T0vertice ) && m.GetEndPoint( 1 ).IsAlmostEqualTo( T1Vertice ) ).Count() == 0
            && T0.Sides.Where( m => m.GetEndPoint( 1 ).IsAlmostEqualTo( T0vertice ) && m.GetEndPoint( 0 ).IsAlmostEqualTo( T1Vertice ) ).Count() == 0 )
          {
            outerVert0 = T0vertice;
            outerVert1 = T1Vertice;
            workedforvertice += 1;
          }
          else
          {
            workedforLine += 1;
          }
        }
        if( workedforvertice == 1 && workedforLine == 2 )
        {
          break;
        }
        else
        {
          workedforvertice = 0;
          workedforLine = 0;
        }
      }

      if( workedforvertice == 1 && workedforLine == 2 )
      {
        FacingCurves = new Curve[2];

        Curve Hyp0 = null;
        Curve Hyp1 = null;

        foreach( Curve side in T0.Sides )
        {
          List<XYZ> ends = new List<XYZ>();
          ends.Add( side.GetEndPoint( 0 ) );
          ends.Add( side.GetEndPoint( 1 ) );

          if( ends.Where( m => m.IsAlmostEqualTo( outerVert0 ) ).Count() == 0 )
          {
            Hyp0 = side;
            break;
          }
        }
        foreach( Curve side in T1.Sides )
        {
          List<XYZ> ends = new List<XYZ>();
          ends.Add( side.GetEndPoint( 0 ) );
          ends.Add( side.GetEndPoint( 1 ) );

          if( ends.Where( m => m.IsAlmostEqualTo( outerVert1 ) ).Count() == 0 )
          {
            Hyp1 = side;
            break;
          }
        }

        FacingCurves[0] = Hyp0;
        FacingCurves[1] = Hyp1;

      }

      return FacingCurves;
    }

    const double _inch = 1.0 / 12.0;
    const double _sixteenth = _inch / 16.0;
    static Curve CreateReversedCurve(

    Curve orig )
    {

      if( orig is Line || orig is Curve )
      {
        return Line.CreateBound(
          orig.GetEndPoint( 1 ),
          orig.GetEndPoint( 0 ) );
      }
      else
      {
        throw new Exception(
          "CreateReversedCurve - Unreachable" );
      }
    }
    public List<Curve> SortCurvesContiguous(
    IList<Curve> curves,
    bool debug_output )
    {
      int n = curves.Count;

      // Walk through each curve (after the first) 
      // to match up the curves in order

      for( int i = 0; i < n; ++i )
      {
        Curve curve = curves[i];
        XYZ endPoint = curve.GetEndPoint( 1 );

        if( debug_output )
        {
          Debug.Print( "{0} endPoint {1}", i,
            Util.PointString( endPoint ) );
        }

        XYZ p;

        // Find curve with start point = end point

        bool found = ( i + 1 >= n );

        for( int j = i + 1; j < n; ++j )
        {
          p = curves[j].GetEndPoint( 0 );

          // If there is a match end->start, 
          // this is the next curve

          if( _sixteenth > p.DistanceTo( endPoint ) )
          {
            if( debug_output )
            {
              Debug.Print(
                "{0} start point, swap with {1}",
                j, i + 1 );
            }

            if( i + 1 != j )
            {
              Curve tmp = curves[i + 1];
              curves[i + 1] = curves[j];
              curves[j] = tmp;
            }
            found = true;
            break;
          }

          p = curves[j].GetEndPoint( 1 );

          // If there is a match end->end, 
          // reverse the next curve

          if( _sixteenth > p.DistanceTo( endPoint ) )
          {
            if( i + 1 == j )
            {
              if( debug_output )
              {
                Debug.Print(
                  "{0} end point, reverse {1}",
                  j, i + 1 );
              }

              curves[i + 1] = CreateReversedCurve(
                 curves[j] );
            }
            else
            {
              if( debug_output )
              {
                Debug.Print(
                  "{0} end point, swap with reverse {1}",
                  j, i + 1 );
              }

              Curve tmp = curves[i + 1];
              curves[i + 1] = CreateReversedCurve(
                 curves[j] );
              curves[j] = tmp;
            }
            found = true;
            break;
          }
        }
        if( !found )
        {
          throw new Exception( "SortCurvesContiguous:"
            + " non-contiguous input curves" );
        }
      }
      return curves.ToList();

    }

    public class PolygonAnyliser
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

        c.AddPolygons( subj, PolyType.ptSubject );

        c.AddPolygons( clip, PolyType.ptClip );

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
}
