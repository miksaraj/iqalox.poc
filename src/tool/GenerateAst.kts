import java.io.PrintWriter
import kotlin.system.exitProcess

fun defineType(writer: PrintWriter, baseName: String, className: String, fieldList: String): Unit {
    writer.println("    class $className(")

    val fields = fieldList.split(", ")
    fields.forEachIndexed { index, it ->
        val type = it.split(" ")[0]
        val name = it.split(" ")[1]
        if (index == fields.size - 1) writer.println("      val $name: $type")
        else writer.println("       val $name: $type,")
    }

    writer.println("    ): $baseName() {")
    writer.println("        override fun <R> accept(visitor: Visitor<R>) = visitor.visit$className$baseName(this)")
    writer.println("    }")
    writer.println()
}

fun defineVisitor(writer: PrintWriter, baseName: String, types: List<String>): Unit {
    writer.println("    interface Visitor<R> {")

    types.forEach { type ->
        val typeName = type.split(":")[0].trim()
        writer.println("        fun visit$typeName$baseName(${baseName.lowercase()}: $typeName): R")
    }
    writer.println("    }")
}

fun defineAst(outputDir: String, baseName: String, types: List<String>): Unit {
    val path = "$outputDir/$baseName.kt"
    val writer = PrintWriter(path, "UTF-8")

    writer.println("package xyz.iqalox.ast")
    writer.println()
    writer.println("/** This file is autogenerated using tool/GenerateAst script. DO NOT EDIT DIRECTLY! */")
    writer.println("abstract class $baseName {")
    writer.println()

    writer.println("    abstract fun <R> accept(visitor: Visitor<R>): R")
    writer.println()
    defineVisitor(writer, baseName, types)

    types.forEach { type ->
        val className = type.split(":")[0].trim()
        val fields = type.split(":")[1].trim()
        defineType(writer, baseName, className, fields)
    }

    writer.print("}")
    writer.close()
}

fun main(args: Array<String>): Unit {
    if (args.size != 1) {
        System.err.println("Usage: GenerateAst.kts <output directory>")
        exitProcess(64)
    }
    val outputDir = args[0]

    defineAst(outputDir, "Expr", listOf(''))

    defineAst(outputDir, "Stmt", listOf(''))
}